﻿using System.Net;
using System.Net.Sockets;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Agent.Exceptions;
using VpnHood.AccessServer.Api;
using VpnHood.Common.Messaging;
using VpnHood.Common.Tokens;
using Token = VpnHood.Common.Tokens.Token;

namespace VpnHood.AccessServer.Test.Dom;

public class AccessTokenDom(TestApp testApp, AccessToken accessToken)
{
    public TestApp TestApp { get; } = testApp;
    public AccessToken AccessToken { get; private set; } = accessToken;
    public Guid AccessTokenId => AccessToken.AccessTokenId;

    public async Task<SessionDom> CreateSession(string? clientId = null, IPAddress? clientIp = null,
        AddressFamily addressFamily = AddressFamily.InterNetwork, bool throwError = true,
        bool autoRedirect = false, string? serverLocation = null, ClientInfo? clientInfo = null, 
        ConnectPlanId planId = ConnectPlanId.Normal)
    {
        // get server ip
        var accessKey = await GetAccessKey();
        var token = Token.FromAccessKey(accessKey);
        var serverEndPoint =
            token.ServerToken.HostEndPoints?.FirstOrDefault(x => x.Address.AddressFamily == addressFamily) ??
            throw new Exception("There is no ApiUrl.");
        return await CreateSession(serverEndPoint, clientId, clientIp, throwError, serverLocation:
            serverLocation, autoRedirect: autoRedirect, clientInfo: clientInfo, planId: planId);
    }

    public async Task<SessionDom> CreateSession(IPEndPoint serverEndPoint, string? clientId = null,
        IPAddress? clientIp = null,
        bool throwError = true, string? serverLocation = null, bool autoRedirect = false, bool allowRedirect = true,
        ClientInfo? clientInfo = null,
        ConnectPlanId planId = ConnectPlanId.Normal)
    {
        // find server of the farm that listen to token EndPoint
        var servers = await TestApp.ServersClient.ListAsync(TestApp.ProjectId);
        var serverData = servers.First(x =>
            x.Server.AccessPoints.Any(accessPoint =>
                new IPEndPoint(IPAddress.Parse(accessPoint.IpAddress), accessPoint.TcpPort).Equals(serverEndPoint)));

        clientIp ??= serverEndPoint.Address.AddressFamily == AddressFamily.InterNetworkV6
            ? TestApp.NewIpV6()
            : TestApp.NewIpV4();

        var sessionRequestEx = await TestApp.CreateSessionRequestEx(
            AccessToken,
            serverEndPoint,
            allowRedirect: allowRedirect,
            locationPath: serverLocation,
            clientInfo: clientInfo,
            clientId: clientId,
            clientIp: clientIp,
            planId: planId);

        // create session
        var ret = await SessionDom.Create(
            TestApp, serverData.Server.ServerId, AccessToken, sessionRequestEx,
            throwError: throwError && !autoRedirect);

        // redirect 
        if (autoRedirect && ret.SessionResponseEx.ErrorCode == SessionErrorCode.RedirectHost) {
            Assert.IsNotNull(ret.SessionResponseEx.RedirectHostEndPoint);
            Assert.IsNotNull(ret.SessionResponseEx.RedirectHostEndPoints);
            Assert.IsTrue(ret.SessionResponseEx.RedirectHostEndPoints.Any(x =>
                x.Equals(ret.SessionResponseEx.RedirectHostEndPoint)));

            // if clientIp is IPv4, then redirectHostEndPoint must be IPv4
            if (ret.SessionRequestEx.HostEndPoint.AddressFamily == AddressFamily.InterNetwork)
                Assert.AreEqual(ret.SessionRequestEx.HostEndPoint.AddressFamily,
                    ret.SessionResponseEx.RedirectHostEndPoint.AddressFamily);

            return await CreateSession(
                ret.SessionResponseEx.RedirectHostEndPoint,
                clientId: sessionRequestEx.ClientInfo.ClientId,
                clientIp: sessionRequestEx.ClientIp,
                serverLocation: serverLocation,
                throwError: throwError,
                planId: planId,
                allowRedirect: false);
        }

        if (throwError && ret.SessionResponseEx.ErrorCode != SessionErrorCode.Ok)
            throw new SessionExceptionEx(
                ret.SessionResponseEx.ErrorCode, 
                ret.SessionResponseEx.ErrorMessage ?? ret.SessionResponseEx.ErrorCode.ToString());

        return ret;
    }

    public async Task<AccessTokenData> Reload()
    {
        var accessTokenData = await TestApp.AccessTokensClient.GetAsync(TestApp.ProjectId, AccessTokenId);
        AccessToken = accessTokenData.AccessToken;
        return accessTokenData;
    }

    public Task<string> GetAccessKey()
    {
        return TestApp.AccessTokensClient.GetAccessKeyAsync(TestApp.ProjectId, AccessToken.AccessTokenId);
    }

    public async Task<Token> GetToken()
    {
        var accessKey = await GetAccessKey();
        var token = Token.FromAccessKey(accessKey);
        return token;
    }

    public async Task Update(AccessTokenUpdateParams updateParams)
    {
        await TestApp.AccessTokensClient.UpdateAsync(TestApp.ProjectId, AccessTokenId, updateParams);
    }
}
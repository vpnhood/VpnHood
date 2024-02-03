﻿using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Sockets;
using VpnHood.AccessServer.Api;
using VpnHood.Common.Messaging;
using Token = VpnHood.Common.Token;

namespace VpnHood.AccessServer.Test.Dom;

public class AccessTokenDom(TestApp testApp, AccessToken accessToken)
{
    public TestApp TestApp { get; } = testApp;
    public AccessToken AccessToken { get; private set; } = accessToken;
    public Guid AccessTokenId => AccessToken.AccessTokenId;

    public async Task<SessionDom> CreateSession(Guid? clientId = null, IPAddress? clientIp = null, AddressFamily addressFamily = AddressFamily.InterNetwork,
        bool assertError = true, bool autoRedirect = false)
    {
        // get server ip
        var accessKey = await GetAccessKey();
        var token = Token.FromAccessKey(accessKey);
        var serverEndPoint = token.ServerToken.HostEndPoints?.FirstOrDefault(x => x.Address.AddressFamily == addressFamily) ?? throw new Exception("There is no HostEndPoint.");
        return await CreateSession(serverEndPoint, clientId, clientIp, assertError, autoRedirect);
    }

    public async Task<SessionDom> CreateSession(IPEndPoint serverEndPoint, Guid? clientId = null, IPAddress? clientIp = null,
        bool assertError = true, bool autoRedirect = false)
    {
        // find server of the farm that listen to token EndPoint
        var servers = await TestApp.ServersClient.ListAsync(TestApp.ProjectId);
        var serverData = servers.First(x =>
            x.Server.AccessPoints.Any(accessPoint =>
                new IPEndPoint(IPAddress.Parse(accessPoint.IpAddress), accessPoint.TcpPort).Equals(serverEndPoint)));

        clientIp ??= serverEndPoint.Address.AddressFamily == AddressFamily.InterNetworkV6
            ? await TestApp.NewIpV6() : await TestApp.NewIpV4();

        var sessionRequestEx = await TestApp.CreateSessionRequestEx(
            AccessToken,
            serverEndPoint,
            clientId,
            clientIp
        );

        // create session
        var ret = await SessionDom.Create(
            TestApp, serverData.Server.ServerId, AccessToken, sessionRequestEx, assertError: assertError && !autoRedirect);

        // redirect 
        if (autoRedirect && ret.SessionResponseEx.ErrorCode == SessionErrorCode.RedirectHost)
        {
            Assert.IsNotNull(ret.SessionResponseEx.RedirectHostEndPoint);
            Assert.AreEqual(ret.SessionRequestEx.HostEndPoint.AddressFamily, ret.SessionResponseEx.RedirectHostEndPoint.AddressFamily);
            return await CreateSession(ret.SessionResponseEx.RedirectHostEndPoint, clientId, clientIp, assertError);
        }

        if (assertError)
            Assert.AreEqual(SessionErrorCode.Ok, ret.SessionResponseEx.ErrorCode, ret.SessionResponseEx.ErrorMessage);

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
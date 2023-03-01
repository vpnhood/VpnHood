using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using VpnHood.AccessServer.Api;
using VpnHood.Common;
using VpnHood.Common.Messaging;

namespace VpnHood.AccessServer.Test.Dom;

public class AccessTokenDom
{
    public TestInit TestInit { get; }
    public AccessToken AccessToken { get; private set; }
    public Guid AccessTokenId => AccessToken.AccessTokenId;

    public AccessTokenDom(TestInit testInit, AccessToken accessToken)
    {
        TestInit = testInit;
        AccessToken = accessToken;
    }

    public async Task<SessionDom> CreateSession(Guid? clientId = null, IPAddress? clientIp = null, AddressFamily addressFamily = AddressFamily.InterNetwork,
        bool assertError = true, bool autoRedirect = false)
    {
        // get server ip
        var accessKey = await GetAccessKey();
        var token = Token.FromAccessKey(accessKey);
        var serverEndPoint = token.HostEndPoints?.FirstOrDefault(x => x.Address.AddressFamily == addressFamily) ?? throw new Exception("There is no HostEndPoint.");
        return await CreateSession(serverEndPoint, clientId, clientIp, assertError, autoRedirect);
    }

    public async Task<SessionDom> CreateSession(IPEndPoint serverEndPoint, Guid? clientId = null, IPAddress? clientIp = null,
        bool assertError = true, bool autoRedirect = false)
    {
        // find server of the farm that listen to token EndPoint
        var servers = await TestInit.ServersClient.ListAsync(TestInit.ProjectId);
        var serverData = servers.First(x =>
            x.Server.AccessPoints.Any(accessPoint =>
                new IPEndPoint(IPAddress.Parse(accessPoint.IpAddress), accessPoint.TcpPort).Equals(serverEndPoint)));

        clientIp ??= serverEndPoint.Address.AddressFamily == AddressFamily.InterNetworkV6
            ? await TestInit.NewIpV6() : await TestInit.NewIpV4();

        var sessionRequestEx = TestInit.CreateSessionRequestEx(
            AccessToken,
            serverEndPoint,
            clientId,
            clientIp
        );

        // create session
        var ret = await SessionDom.Create(
            TestInit, serverData.Server.ServerId, AccessToken, sessionRequestEx, assertError: assertError && !autoRedirect);

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
        var accessTokenData = await TestInit.AccessTokensClient.GetAsync(TestInit.ProjectId, AccessTokenId);
        AccessToken = accessTokenData.AccessToken;
        return accessTokenData;
    }

    public async Task<string> GetAccessKey()
    {
        return await TestInit.AccessTokensClient.GetAccessKeyAsync(TestInit.ProjectId, AccessToken.AccessTokenId);
    }

    public async Task<Token> GetToken()
    {
        var accessKey = await GetAccessKey();
        var token = Token.FromAccessKey(accessKey);
        return token;
    }

    public async Task Update(AccessTokenUpdateParams updateParams)
    {
        await TestInit.AccessTokensClient.UpdateAsync(TestInit.ProjectId, AccessTokenId, updateParams);
    }
}
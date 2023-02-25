using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using VpnHood.AccessServer.Api;
using VpnHood.Common;

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

    public async Task<SessionDom> CreateSession(Guid? clientId = null, IPAddress? clientIp = null, bool assertError = true)
    {
        // get server ip
        var accessKey = await GetAccessKey();
        var token = Token.FromAccessKey(accessKey);
        var serverEndPoint = token.HostEndPoints?.FirstOrDefault() ?? throw new Exception("There is no HostEndPoint.");

        // find server of the farm that listen to token EndPoint
        var servers = await TestInit.ServersClient.ListAsync(TestInit.ProjectId);
        var serverData = servers.Single(x =>
            x.Server.AccessPoints.Any(accessPoint =>
                accessPoint.AccessPointMode is AccessPointMode.Public or AccessPointMode.PublicInToken &&
                accessPoint.IpAddress == serverEndPoint.Address.ToString() &&
                accessPoint.TcpPort == serverEndPoint.Port));

        var sessionRequestEx = TestInit.CreateSessionRequestEx(
            AccessToken,
            clientId,
            serverEndPoint,
            clientIp ?? await TestInit.NewIpV4()
            );

        var ret = await SessionDom.Create(
            TestInit, serverData.Server.ServerId, AccessToken,
            sessionRequestEx, assertError: assertError);

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

    public async Task Update(AccessTokenUpdateParams updateParams)
    {
        await TestInit.AccessTokensClient.UpdateAsync(TestInit.ProjectId, AccessTokenId, updateParams);
    }
}
using System;
using System.Linq;
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

    public async Task<SessionDom> CreateSession(Guid? clientId = null, bool assertError = true)
    {
        // get server ip
        var accessKey = await TestInit.AccessTokensClient.GetAccessKeyAsync(TestInit.ProjectId, AccessToken.AccessTokenId);
        var token = Token.FromAccessKey(accessKey);
        var serverEndPoint = token.HostEndPoints?.FirstOrDefault() ?? throw new Exception("There is no HostEndPoint.");

        // find server by ip
        var accessPoints = await TestInit.AccessPointsClient.ListAsync(TestInit.ProjectId, accessPointGroupId: AccessToken.AccessPointGroupId);
        var accessPoint = accessPoints.First(x => 
            x.IpAddress==serverEndPoint.Address.ToString() && x.TcpPort == serverEndPoint.Port && 
            x.AccessPointMode is AccessPointMode.Public or AccessPointMode.PublicInToken );

        var sessionRequestEx = TestInit.CreateSessionRequestEx(
            AccessToken,
            clientId,
            token.HostEndPoints?.First(),
            await TestInit.NewIpV4());

        var ret = await SessionDom.Create(TestInit, accessPoint.ServerId, AccessToken, 
            sessionRequestEx, assertError: assertError);
        return ret;
    }

    public async Task Reload()
    {
        var accessTokenData = await TestInit.AccessTokensClient.GetAsync(TestInit.ProjectId, AccessTokenId);
        AccessToken = accessTokenData.AccessToken;
    }
}
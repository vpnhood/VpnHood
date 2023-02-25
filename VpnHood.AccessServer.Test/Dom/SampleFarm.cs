using System.Linq;
using System.Threading.Tasks;
using VpnHood.AccessServer.Api;

namespace VpnHood.AccessServer.Test.Dom;

public class SampleFarm : AccessPointGroupDom
{
    private SampleFarm(
        TestInit testInit,
        AccessPointGroup accessPointGroup,
        ServerDom server1,
        ServerDom server2,
        AccessToken publicToken1,
        AccessToken publicToken2,
        AccessToken privateToken1,
        AccessToken privateToken2) 
        : base(testInit, accessPointGroup)
    {
        Server1 = server1;
        Server2 = server2;
        PublicToken1 = publicToken1;
        PublicToken2 = publicToken2;
        PrivateToken1 = privateToken1;
        PrivateToken2 = privateToken2;
    }

    public ServerDom Server1 { get; }
    public ServerDom Server2 { get; }
    public AccessToken PublicToken1 { get; }
    public AccessToken PublicToken2 { get; }
    public AccessToken PrivateToken1 { get; }
    public AccessToken PrivateToken2 { get; }

    public static async Task<SampleFarm> Create(TestInit testInit)
    {
        var accessPointGroup = await testInit.AccessPointGroupsClient.CreateAsync(testInit.ProjectId, new AccessPointGroupCreateParams());

        // create servers
        var sampleServers = new[]
        {
            await ServerDom.Create(testInit, accessPointGroup.AccessPointGroupId),
            await ServerDom.Create(testInit, accessPointGroup.AccessPointGroupId)
        };

        // create accessTokens
        var accessTokens = new[]
        {
            await testInit.AccessTokensClient.CreateAsync(testInit.ProjectId,
                new AccessTokenCreateParams {AccessPointGroupId = accessPointGroup.AccessPointGroupId, IsPublic = true}),
            await testInit.AccessTokensClient.CreateAsync(testInit.ProjectId,
                new AccessTokenCreateParams {AccessPointGroupId = accessPointGroup.AccessPointGroupId, IsPublic = true}),
            await testInit.AccessTokensClient.CreateAsync(testInit.ProjectId,
                new AccessTokenCreateParams {AccessPointGroupId = accessPointGroup.AccessPointGroupId, IsPublic = false}),
            await testInit.AccessTokensClient.CreateAsync(testInit.ProjectId,
                new AccessTokenCreateParams {AccessPointGroupId = accessPointGroup.AccessPointGroupId, IsPublic = false})
        };

        var sampleFarm = new SampleFarm(
            testInit: testInit,
            accessPointGroup: accessPointGroup,
            server1: sampleServers[0],
            server2: sampleServers[1],
            publicToken1: accessTokens[0],
            publicToken2: accessTokens[1],
            privateToken1: accessTokens[2],
            privateToken2: accessTokens[3]
            );

        // create 2 sessions per each token
        var sessionTasks1 = accessTokens.Select(x => sampleFarm.Server1.CreateSession(x));
        var sessionTasks2 = accessTokens.Select(x => sampleFarm.Server2.CreateSession(x));
        await Task.WhenAll(sessionTasks1.Concat(sessionTasks2));
        return sampleFarm;
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VpnHood.AccessServer.Api;

namespace VpnHood.AccessServer.Test.Sampler;

public class SampleAccessPointGroup
{
    public TestInit TestInit { get; }
    public AccessPointGroup AccessPointGroup { get; }
    public List<SampleServer> SampleServers { get; } = new();
    public Guid AccessPointGroupId => AccessPointGroup.AccessPointGroupId;
    public DateTime CreatedTime { get; } = DateTime.UtcNow;
    public Guid ProjectId => TestInit.ProjectId;

    protected SampleAccessPointGroup(TestInit testInit, AccessPointGroup accessPointGroup)
    {
        TestInit = testInit;
        AccessPointGroup = accessPointGroup;
    }

    public async Task<SampleAccessToken> CreateAccessToken(bool isPublic)
    {
        var ret = await TestInit.AccessTokenClient.CreateAsync(TestInit.ProjectId,
            new AccessTokenCreateParams
            { AccessPointGroupId = AccessPointGroup.AccessPointGroupId, IsPublic = isPublic });
        return new SampleAccessToken(TestInit, ret);
    }

    public async Task<SampleServer> AddNewServer(bool configure = true)
    {
        var sampleServer = await SampleServer.Create(TestInit, AccessPointGroupId, configure);
        SampleServers.Add(sampleServer);
        return sampleServer;
    }


    public static async Task<SampleAccessPointGroup> Create(TestInit? testInit = null, int serverCount = 1)
    {
        testInit ??= await TestInit.Create(createServers: false);
        var accessPointGroup = await testInit.ServerFarmClient.CreateAsync(testInit.ProjectId, new AccessPointGroupCreateParams());
        var ret = new SampleAccessPointGroup(testInit, accessPointGroup);

        for (var i = 0; i < serverCount; i++)
            await ret.AddNewServer();
        return ret;
    }
}
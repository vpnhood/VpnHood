using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VpnHood.AccessServer.Api;

namespace VpnHood.AccessServer.Test.Dom;

public class AccessPointGroupDom
{
    public TestInit TestInit { get; }
    public AccessPointGroup AccessPointGroup { get; }
    public List<ServerDom> SampleServers { get; } = new();
    public Guid AccessPointGroupId => AccessPointGroup.AccessPointGroupId;
    public DateTime CreatedTime { get; } = DateTime.UtcNow;
    public Guid ProjectId => TestInit.ProjectId;

    protected AccessPointGroupDom(TestInit testInit, AccessPointGroup accessPointGroup)
    {
        TestInit = testInit;
        AccessPointGroup = accessPointGroup;
    }

    public async Task<AccessTokenDom> CreateAccessToken(bool isPublic)
    {
        var ret = await TestInit.AccessTokensClient.CreateAsync(TestInit.ProjectId,
            new AccessTokenCreateParams
            {
                AccessPointGroupId = AccessPointGroup.AccessPointGroupId,
                IsPublic = isPublic
            }
            );
        return new AccessTokenDom(TestInit, ret);
    }

    public async Task<AccessTokenDom> CreateAccessToken(AccessTokenCreateParams createParams)
    {
        if (createParams.AccessPointGroupId != Guid.Empty)
            throw new InvalidOperationException($"{nameof(AccessPointGroupId)} must be an empty guid or current id!");
        createParams.AccessPointGroupId = AccessPointGroup.AccessPointGroupId;

        var ret = await TestInit.AccessTokensClient.CreateAsync(TestInit.ProjectId, createParams);
        return new AccessTokenDom(TestInit, ret);
    }


    public async Task<ServerDom> AddNewServer(bool configure = true)
    {
        var sampleServer = await ServerDom.Create(TestInit, AccessPointGroupId, configure);
        SampleServers.Add(sampleServer);
        return sampleServer;
    }


    public static async Task<AccessPointGroupDom> Create(TestInit? testInit = null, int serverCount = 1)
    {
        testInit ??= await TestInit.Create(createServers: false);
        var accessPointGroup = await testInit.AccessPointGroupsClient.CreateAsync(testInit.ProjectId, new AccessPointGroupCreateParams());
        var ret = new AccessPointGroupDom(testInit, accessPointGroup);

        for (var i = 0; i < serverCount; i++)
            await ret.AddNewServer();
        return ret;
    }
}
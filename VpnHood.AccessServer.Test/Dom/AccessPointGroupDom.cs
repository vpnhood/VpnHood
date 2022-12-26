using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VpnHood.AccessServer.Api;

namespace VpnHood.AccessServer.Test.Dom;

public class AccessPointGroupDom
{
    public TestInit TestInit { get; }
    public AccessPointGroup AccessPointGroup { get; private set; }
    public List<ServerDom> Servers { get; } = new();
    public Guid AccessPointGroupId => AccessPointGroup.AccessPointGroupId;
    public DateTime CreatedTime { get; } = DateTime.UtcNow;
    public Guid ProjectId => TestInit.ProjectId;
    public ServerDom DefaultServer => Servers.First();

    protected AccessPointGroupDom(TestInit testInit, AccessPointGroup accessPointGroup)
    {
        TestInit = testInit;
        AccessPointGroup = accessPointGroup;
    }

    public async Task Reload()
    {
        AccessPointGroup = await TestInit.AccessPointGroupsClient.GetAsync(ProjectId, AccessPointGroupId);
    }

    public async Task<AccessTokenDom> CreateAccessToken(bool isPublic)
    {
        var ret = await TestInit.AccessTokensClient.CreateAsync(TestInit.ProjectId,
            new AccessTokenCreateParams
            {
                AccessPointGroupId = AccessPointGroup.AccessPointGroupId,
                IsPublic = isPublic
            });

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
        Servers.Add(sampleServer);
        return sampleServer;
    }

    public static async Task<AccessPointGroupDom> Create(TestInit? testInit = null, int serverCount = 1, string? name = null)
    {
        testInit ??= await TestInit.Create(createServers: false);
        name ??= Guid.NewGuid().ToString();

        var accessPointGroup = await testInit.AccessPointGroupsClient.CreateAsync(testInit.ProjectId, new AccessPointGroupCreateParams
        {
            AccessPointGroupName = name
        });
        
        var ret = new AccessPointGroupDom(testInit, accessPointGroup);
        for (var i = 0; i < serverCount; i++)
            await ret.AddNewServer();

        return ret;
    }

    public async Task<AccessPoint[]> GetAccessPoints()
    {
        var accessPoints = await TestInit.AccessPointsClient
            .ListAsync(TestInit.ProjectId, accessPointGroupId: AccessPointGroupId);

        return accessPoints.ToArray();
    }

}
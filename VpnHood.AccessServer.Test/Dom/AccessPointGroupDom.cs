using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using VpnHood.AccessServer.Api;

namespace VpnHood.AccessServer.Test.Dom;

public class AccessPointGroupDom
{
    public TestInit TestInit { get; }
    public AccessPointGroup AccessPointGroup { get; private set; }
    public List<ServerDom> Servers { get; private set; } = new();
    public Guid AccessPointGroupId => AccessPointGroup.AccessPointGroupId;
    public DateTime CreatedTime { get; } = DateTime.UtcNow;
    public Guid ProjectId => TestInit.ProjectId;
    public ServerDom DefaultServer => Servers.First();

    protected AccessPointGroupDom(TestInit testInit, AccessPointGroup accessPointGroup)
    {
        TestInit = testInit;
        AccessPointGroup = accessPointGroup;
    }

    public static async Task<AccessPointGroupDom> Create(TestInit? testInit = default, AccessPointGroupCreateParams? createParams = default, int serverCount = 1)
    {
        testInit ??= await TestInit.Create(createServers: false);
        createParams ??= new AccessPointGroupCreateParams
        {
            AccessPointGroupName = Guid.NewGuid().ToString()
        };

        var accessPointGroup = await testInit.AccessPointGroupsClient.CreateAsync(testInit.ProjectId, createParams);

        var ret = new AccessPointGroupDom(testInit, accessPointGroup);
        for (var i = 0; i < serverCount; i++)
            await ret.AddNewServer();

        return ret;
    }

    public static async Task<AccessPointGroupDom> Attach(TestInit testInit, Guid serverFarmId)
    {
        var serverFarmData = await testInit.AccessPointGroupsClient.GetAsync(testInit.ProjectId, serverFarmId);
        var serverFarm = new AccessPointGroupDom(testInit, serverFarmData.ServerFarm);
        await serverFarm.ReattachServers();
        return serverFarm;
    }


    public async Task<ServerFarmData> Reload()
    {
        var serverFarmData = await TestInit.AccessPointGroupsClient.GetAsync(ProjectId, AccessPointGroupId, includeSummary: true);
        AccessPointGroup = serverFarmData.ServerFarm;
        return serverFarmData;
    }

    public async Task ReattachServers()
    {
        var servers = await TestInit.ServersClient.ListAsync(TestInit.ProjectId, serverFarmId: AccessPointGroupId);
        Servers = servers.Select(serverData => ServerDom.Attach(TestInit, serverData.Server)).ToList();
    }

    public async Task ReloadServers()
    {
        foreach (var server in Servers)
            await server.Reload();
    }


    public async Task<AccessTokenDom> CreateAccessToken(bool isPublic = false)
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
        if (createParams.AccessPointGroupId != Guid.Empty && createParams.AccessPointGroupId != AccessPointGroupId)
            throw new InvalidOperationException($"{nameof(AccessPointGroupId)} must be an empty guid or current id!");

        createParams.AccessPointGroupId = AccessPointGroup.AccessPointGroupId;

        var ret = await TestInit.AccessTokensClient.CreateAsync(TestInit.ProjectId, createParams);
        return new AccessTokenDom(TestInit, ret);
    }

    public async Task<ServerDom> AddNewServer(int sessionCount)
    {
        var serverDom = await AddNewServer(true, false);
        serverDom.ServerStatus.SessionCount = 0;
        await serverDom.SendStatus();
        return serverDom;
    }

    public async Task<ServerDom> AddNewServer(bool configure = true, bool sendStatus = true)
    {
        var sampleServer = await ServerDom.Create(TestInit, AccessPointGroupId, configure, sendStatus);
        Servers.Add(sampleServer);
        return sampleServer;
    }

    public async Task<ServerDom> AddNewServer(ServerCreateParams createParams, bool configure = true, bool sendStatus = true)
    {
        // ReSharper disable once LocalizableElement
        if (createParams.AccessPointGroupId != AccessPointGroupId && createParams.AccessPointGroupId != Guid.Empty)
            throw new ArgumentException($"{nameof(createParams.AccessPointGroupId)} must be the same as this farm", nameof(createParams));

        createParams.AccessPointGroupId = AccessPointGroupId;
        var sampleServer = await ServerDom.Create(TestInit, createParams, configure, sendStatus);
        Servers.Add(sampleServer);
        return sampleServer;
    }

    public ServerDom FindServerByEndPoint(IPEndPoint ipEndPoint)
    {
        var serverDom = Servers.Single(x => x.Server.AccessPoints.Any(accessPoint =>
            new IPEndPoint(IPAddress.Parse(accessPoint.IpAddress), accessPoint.TcpPort).Equals(ipEndPoint)));

        return serverDom;
    }
}
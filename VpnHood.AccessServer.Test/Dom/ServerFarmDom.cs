using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using VpnHood.AccessServer.Api;

namespace VpnHood.AccessServer.Test.Dom;

public class ServerFarmDom
{
    public TestInit TestInit { get; }
    public ServerFarm ServerFarm { get; private set; }
    public List<ServerDom> Servers { get; private set; } = new();
    public Guid ServerFarmId => ServerFarm.ServerFarmId;
    public DateTime CreatedTime { get; } = DateTime.UtcNow;
    public Guid ProjectId => TestInit.ProjectId;
    public ServerDom DefaultServer => Servers.First();

    protected ServerFarmDom(TestInit testInit, ServerFarm serverFarm)
    {
        TestInit = testInit;
        ServerFarm = serverFarm;
    }

    public static async Task<ServerFarmDom> Create(TestInit? testInit = default, ServerFarmCreateParams? createParams = default, int serverCount = 1)
    {
        testInit ??= await TestInit.Create(createServers: false);
        createParams ??= new ServerFarmCreateParams
        {
            ServerFarmName = Guid.NewGuid().ToString()
        };

        var serverFarm = await testInit.ServerFarmsClient.CreateAsync(testInit.ProjectId, createParams);

        var ret = new ServerFarmDom(testInit, serverFarm);
        for (var i = 0; i < serverCount; i++)
            await ret.AddNewServer();

        return ret;
    }

    public static async Task<ServerFarmDom> Attach(TestInit testInit, Guid serverFarmId)
    {
        var serverFarmData = await testInit.ServerFarmsClient.GetAsync(testInit.ProjectId, serverFarmId);
        var serverFarm = new ServerFarmDom(testInit, serverFarmData.ServerFarm);
        await serverFarm.ReattachServers();
        return serverFarm;
    }


    public async Task<ServerFarmData> Reload()
    {
        var serverFarmData = await TestInit.ServerFarmsClient.GetAsync(ProjectId, ServerFarmId, includeSummary: true);
        ServerFarm = serverFarmData.ServerFarm;
        return serverFarmData;
    }

    public async Task ReattachServers()
    {
        var servers = await TestInit.ServersClient.ListAsync(TestInit.ProjectId, serverFarmId: ServerFarmId);
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
                ServerFarmId = ServerFarm.ServerFarmId,
                IsPublic = isPublic
            });

        return new AccessTokenDom(TestInit, ret);
    }

    public async Task<AccessTokenDom> CreateAccessToken(AccessTokenCreateParams createParams)
    {
        if (createParams.ServerFarmId != Guid.Empty && createParams.ServerFarmId != ServerFarmId)
            throw new InvalidOperationException($"{nameof(ServerFarmId)} must be an empty guid or current id!");

        createParams.ServerFarmId = ServerFarm.ServerFarmId;

        var ret = await TestInit.AccessTokensClient.CreateAsync(TestInit.ProjectId, createParams);
        return new AccessTokenDom(TestInit, ret);
    }

    public async Task<ServerDom> AddNewServer(bool configure = true, bool sendStatus = true)
    {
        var sampleServer = await ServerDom.Create(TestInit, ServerFarmId, configure, sendStatus);
        Servers.Add(sampleServer);
        return sampleServer;
    }

    public async Task<ServerDom> AddNewServer(ServerCreateParams createParams, bool configure = true, bool sendStatus = true)
    {
        // ReSharper disable once LocalizableElement
        if (createParams.ServerFarmId != ServerFarmId && createParams.ServerFarmId != Guid.Empty)
            throw new ArgumentException($"{nameof(createParams.ServerFarmId)} must be the same as this farm", nameof(createParams));

        createParams.ServerFarmId = ServerFarmId;
        var sampleServer = await ServerDom.Create(TestInit, createParams, configure, sendStatus);
        Servers.Add(sampleServer);
        return sampleServer;
    }

    public ServerDom FindServerByEndPoint(IPEndPoint ipEndPoint)
    {
        var serverDom = Servers.First(x =>
            x.Server.AccessPoints.Any(accessPoint => 
                new IPEndPoint(IPAddress.Parse(accessPoint.IpAddress), accessPoint.TcpPort).Equals(ipEndPoint)));

        return serverDom;
    }
}
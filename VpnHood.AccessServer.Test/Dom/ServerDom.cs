using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VpnHood.Server;
using VpnHood.AccessServer.Api;
using System.Net;
using System.Data;

namespace VpnHood.AccessServer.Test.Dom;

public class ServerDom
{
    public TestInit TestInit { get; }
    public AgentClient AgentClient { get; }
    public Api.Server Server { get; private set; }
    public List<SessionDom> Sessions { get; } = new();
    public ServerInfo ServerInfo { get; set; }
    public Server.Configurations.ServerConfig ServerConfig { get; private set; } = default!;
    public Guid ServerId => Server.ServerId;

    public ServerDom(TestInit testInit, Api.Server server, ServerInfo serverInfo)
    {
        TestInit = testInit;
        Server = server;
        ServerInfo = serverInfo;
        AgentClient = testInit.CreateAgentClient(server.ServerId);
    }

    public static async Task<ServerDom> Attach(TestInit testInit, Guid serverId)
    {
        var serverData = await testInit.ServersClient.GetAsync(testInit.ProjectId, serverId);
        return Attach(testInit, serverData.Server);
    }

    public static ServerDom Attach(TestInit testInit, Api.Server server)
    {
        var serverInfo = new ServerInfo(
            version: server.Version != null ? Version.Parse(server.Version) : new Version(),
            environmentVersion: server.EnvironmentVersion != null ? Version.Parse(server.EnvironmentVersion) : new Version(),
            privateIpAddresses: Array.Empty<IPAddress>(),
            publicIpAddresses: Array.Empty<IPAddress>(),
            status: new ServerStatus
            {
                AvailableMemory = server.ServerStatus?.AvailableMemory ?? 0,
                ConfigCode = Guid.Empty.ToString(),
                CpuUsage = server.ServerStatus?.CpuUsage ?? 0,
                SessionCount = server.ServerStatus?.SessionCount ?? 0,
                TcpConnectionCount = server.ServerStatus?.TcpConnectionCount ?? 0,
                ThreadCount = server.ServerStatus?.ThreadCount ?? 0,
                TunnelReceiveSpeed = server.ServerStatus?.TunnelReceiveSpeed ?? 0,
                TunnelSendSpeed = server.ServerStatus?.TunnelSendSpeed ?? 0,
                UdpConnectionCount = server.ServerStatus?.UdpConnectionCount ?? 0,
                UsedMemory = server is { TotalMemory: { }, ServerStatus.AvailableMemory: { } }
                    ? server.TotalMemory.Value - server.ServerStatus.AvailableMemory.Value
                    : 0
            })
        {
            MachineName = server.MachineName,
            LogicalCoreCount = server.LogicalCoreCount ?? 0,
            LastError = server.LastConfigError,
            OsInfo = server.OsInfo,
            TotalMemory = server.TotalMemory
        };

        var serverDom = new ServerDom(testInit, server, serverInfo);
        return serverDom;
    }

    public async Task Reload()
    {
        var serverData = await TestInit.ServersClient.GetAsync(TestInit.ProjectId, ServerId);
        Server = serverData.Server;
    }

    public static async Task<ServerDom> Create(TestInit testInit, ServerCreateParams createParams, bool autoConfigure = true)
    {
        var server = await testInit.ServersClient.CreateAsync(testInit.ProjectId, createParams);

        var myServer = new ServerDom(
            testInit: testInit,
            server: server,
            serverInfo: await testInit.NewServerInfo()
            );

        if (autoConfigure)
        {
            await myServer.Configure();
            await myServer.Reload();
        }

        return myServer;
    }

    public static Task<ServerDom> Create(TestInit testInit, Guid accessPointGroupId, bool autoConfigure = true)
    {
        return Create(testInit, new ServerCreateParams { AccessPointGroupId = accessPointGroupId }, autoConfigure);
    }

    public async Task Update(ServerUpdateParams updateParams)
    {
        Server = await TestInit.ServersClient.UpdateAsync(TestInit.ProjectId, ServerId, updateParams);
    }

    public async Task Configure(bool updateStatus = true)
    {
        ServerConfig = await AgentClient.Server_Configure(ServerInfo);
        if (updateStatus)
            await UpdateStatus(ServerInfo.Status);
    }

    public async Task<ServerCommand> UpdateStatus(ServerStatus serverStatus, bool overwriteConfigCode = true)
    {
        if (overwriteConfigCode) serverStatus.ConfigCode = ServerConfig.ConfigCode;
        return await AgentClient.Server_UpdateStatus(serverStatus);
    }

    public async Task<SessionDom> CreateSession(AccessToken accessToken, Guid? clientId = null)
    {
        var sessionRequestEx = TestInit.CreateSessionRequestEx(
            accessToken,
            clientId,
            ServerConfig.TcpEndPointsValue.First(),
            await TestInit.NewIpV4());

        var testSession = await SessionDom.Create(TestInit, ServerId, accessToken, sessionRequestEx, AgentClient);
        Sessions.Add(testSession);
        return testSession;
    }
}
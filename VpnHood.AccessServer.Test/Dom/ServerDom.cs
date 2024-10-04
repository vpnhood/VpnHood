using System.Net;
using GrayMint.Common.Utils;
using VpnHood.AccessServer.Api;
using VpnHood.Common.Messaging;
using VpnHood.Server.Access;
using VpnHood.Server.Access.Configurations;

namespace VpnHood.AccessServer.Test.Dom;

public class ServerDom(TestApp testApp, VpnServer server, ServerInfo serverInfo)
{
    public TestApp TestApp { get; } = testApp;
    public ServersClient Client => TestApp.ServersClient;
    public AgentClient AgentClient { get; } = testApp.CreateAgentClient(server.ServerId);
    public VpnServer Server { get; private set; } = server;
    public List<SessionDom> Sessions { get; } = [];
    public ServerInfo ServerInfo { get; set; } = serverInfo;
    public ServerStatus ServerStatus => ServerInfo.Status;
    public ServerConfig ServerConfig { get; private set; } = default!;
    public Guid ServerId => Server.ServerId;

    public static async Task<ServerDom> Attach(TestApp testApp, Guid serverId)
    {
        var serverData = await testApp.ServersClient.GetAsync(testApp.ProjectId, serverId);
        return Attach(testApp, serverData.Server);
    }

    public static ServerDom Attach(TestApp testApp, VpnServer server)
    {
        var serverInfo = new ServerInfo {
            Version = server.Version != null ? Version.Parse(server.Version) : new Version(),
            EnvironmentVersion = server.EnvironmentVersion != null
                ? Version.Parse(server.EnvironmentVersion)
                : new Version(),
            PrivateIpAddresses = [],
            PublicIpAddresses = [],
            MachineName = server.MachineName,
            LogicalCoreCount = server.LogicalCoreCount ?? 0,
            OsInfo = server.OsInfo,
            TotalMemory = server.TotalMemory,
            Status = new ServerStatus {
                AvailableMemory = server.ServerStatus?.AvailableMemory ?? 0,
                ConfigCode = Guid.Empty.ToString(),
                CpuUsage = server.ServerStatus?.CpuUsage ?? 0,
                SessionCount = server.ServerStatus?.SessionCount ?? 0,
                TcpConnectionCount = server.ServerStatus?.TcpConnectionCount ?? 0,
                ThreadCount = server.ServerStatus?.ThreadCount ?? 0,
                TunnelSpeed = new Traffic {
                    Sent = server.ServerStatus?.TunnelReceiveSpeed ?? 0,
                    Received = server.ServerStatus?.TunnelSendSpeed ?? 0
                },
                ConfigError = server.LastConfigError,
                UdpConnectionCount = server.ServerStatus?.UdpConnectionCount ?? 0,
                UsedMemory = server is { TotalMemory: not null, ServerStatus.AvailableMemory: not null }
                    ? server.TotalMemory.Value - server.ServerStatus.AvailableMemory.Value
                    : 0
            }
        };

        var serverDom = new ServerDom(testApp, server, serverInfo);
        return serverDom;
    }

    public async Task Reload()
    {
        var serverData = await TestApp.ServersClient.GetAsync(TestApp.ProjectId, ServerId);
        Server = serverData.Server;
    }

    public static async Task<ServerDom> Create(TestApp testApp, ServerCreateParams createParams,
        bool configure = true, bool sendStatus = true, int? logicalCore = null, IPAddress? publicIpV4 = null)
    {
        var serverData = await testApp.ServersClient.CreateAsync(testApp.ProjectId, createParams);

        var myServer = new ServerDom(
            testApp: testApp,
            server: serverData.Server,
            serverInfo: testApp.NewServerInfo(
                randomStatus: false,
                logicalCore: logicalCore,
                publicIpV4: publicIpV4)
        );

        if (configure) {
            await myServer.Configure(sendStatus);
            await myServer.Reload();
        }

        return myServer;
    }

    public static async Task<ServerDom> Create(TestApp testApp, Guid serverFarmId, bool configure = true,
        bool sendStatus = true, IPAddress? gatewayIpV4 = null, int? logicalCore = null)
    {
        var serverDom = await Create(testApp,
            new ServerCreateParams {
                ServerFarmId = serverFarmId
            },
            configure: configure,
            sendStatus: sendStatus,
            logicalCore: logicalCore,
            publicIpV4: gatewayIpV4);

        return serverDom;
    }

    public async Task Update(ServerUpdateParams updateParams)
    {
        var serverData = await TestApp.ServersClient.UpdateAsync(TestApp.ProjectId, ServerId, updateParams);
        Server = serverData.Server;
    }

    public async Task Configure(bool updateStatus = true)
    {
        ServerConfig = await AgentClient.Server_Configure(ServerInfo);
        if (updateStatus)
            await SendStatus(ServerInfo.Status);
    }

    public Task<ServerCommand> SendStatus(bool overwriteConfigCode = true)
    {
        if (overwriteConfigCode)
            ServerInfo.Status.ConfigCode = ServerConfig.ConfigCode;
        return AgentClient.Server_UpdateStatus(ServerInfo.Status);
    }

    public Task<ServerCommand> SendStatus(ServerStatus serverStatus, bool overwriteConfigCode = true)
    {
        if (overwriteConfigCode) serverStatus.ConfigCode = ServerConfig.ConfigCode;
        ServerInfo.Status = serverStatus;
        return AgentClient.Server_UpdateStatus(serverStatus);
    }

    public async Task<SessionDom> CreateSession(AccessToken accessToken, Guid? clientId = null, bool assertError = true)
    {
        var sessionRequestEx = await TestApp.CreateSessionRequestEx(
            accessToken,
            ServerConfig.TcpEndPointsValue.First(),
            clientId,
            TestApp.NewIpV4());

        var testSession =
            await SessionDom.Create(TestApp, ServerId, accessToken, sessionRequestEx, AgentClient, assertError);
        Sessions.Add(testSession);
        return testSession;
    }

    public Task Delete()
    {
        return Client.DeleteAsync(TestApp.ProjectId, ServerId);
    }

    public async Task WaitForState(ServerState state)
    {
        await TestUtil.AssertEqualsWait(state, async () => {
            await Reload();
            return Server.ServerState;
        });
    }

    public Task SetHostPanelDomain(string domainName)
    {
        return Update(new ServerUpdateParams {
            HostPanelUrl = new PatchOfUri { Value = new Uri($"https://{domainName}") }
        });
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VpnHood.AccessServer.Api;
using VpnHood.Server;

namespace VpnHood.AccessServer.Test.Sampler;

public class SampleServer
{
    public TestInit TestInit { get; }
    public AgentClient AgentClient { get; }
    public Server2 Server { get; }
    public List<SampleSession> Sessions { get; } = new();
    public ServerInfo ServerInfo { get; }
    public ServerConfig ServerConfig { get; private set; } = default!;
    public Guid ServerId => Server.ServerId;

    public SampleServer(TestInit testInit, AgentClient agentClient, Server2 server, ServerInfo serverInfo)
    {
        TestInit = testInit;
        AgentClient = agentClient;
        Server = server;
        ServerInfo = serverInfo;
    }

    public static async Task<SampleServer> Create(TestInit testInit, Guid accessPointGroupId, bool configure = true)
    {
        var server = await testInit.ServersClient.CreateAsync(testInit.ProjectId, new ServerCreateParams { AccessPointGroupId = accessPointGroupId });
        var myServer = new SampleServer(
            testInit: testInit,
            agentClient: testInit.CreateAgentClient(server.ServerId),
            server: server,
            serverInfo: await testInit.NewServerInfo()
        );

        if (configure)
            await myServer.Configure();

        return myServer;
    }

    public async Task Configure()
    {
        ServerConfig = await AgentClient.Server_Configure(ServerInfo);
        ServerInfo.Status.ConfigCode = ServerConfig.ConfigCode;
        await AgentClient.Server_UpdateStatus(ServerInfo.Status);
    }

    public async Task<ServerCommand> UpdateStatus(ServerStatus serverStatus, bool overwriteConfigCode = true)
    {
        if (overwriteConfigCode) serverStatus.ConfigCode = ServerConfig.ConfigCode;
        return await AgentClient.Server_UpdateStatus(serverStatus);
    }

    public async Task<SampleSession> AddSession(AccessToken accessToken, Guid? clientId = null)
    {
        var sessionRequestEx = TestInit.CreateSessionRequestEx(
            accessToken,
            clientId,
            ServerConfig.TcpEndPoints.First(),
            await TestInit.NewIpV4());

        var testSession = await SampleSession.Create(TestInit, ServerId, accessToken, sessionRequestEx, AgentClient);
        Sessions.Add(testSession);
        return testSession;
    }
}
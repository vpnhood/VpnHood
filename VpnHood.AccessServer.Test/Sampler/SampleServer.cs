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
    public Api.Server Server { get; }
    public List<SampleSession> Sessions { get; } = new();
    public ServerInfo ServerInfo { get; }
    public ServerConfig ServerConfig { get; private set; } = default!;
    public Guid ServerId => Server.ServerId;

    public SampleServer(TestInit testInit, AgentClient agentClient, Api.Server server, ServerInfo serverInfo)
    {
        TestInit = testInit;
        AgentClient = agentClient;
        Server = server;
        ServerInfo = serverInfo;
    }

    public static async Task<SampleServer> Create(TestInit testInit, Guid farmId)
    {
        var server = await testInit.ServerClient.CreateAsync(testInit.ProjectId, new ServerCreateParams { AccessPointGroupId = farmId });
        var myServer = new SampleServer(
            testInit: testInit,
            agentClient: testInit.CreateAgentClient(server.ServerId),
            server: server,
            serverInfo: await testInit.NewServerInfo()
        );

        myServer.ServerConfig = await myServer.AgentClient.Server_Configure(myServer.ServerInfo);
        myServer.ServerInfo.Status.ConfigCode = myServer.ServerConfig.ConfigCode;
        await myServer.AgentClient.Server_UpdateStatus(myServer.ServerInfo.Status);

        return myServer;
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
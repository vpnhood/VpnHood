using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using VpnHood.AccessServer.Api;

namespace VpnHood.AccessServer.Test;

public class SampleFarm
{
    public class SampleSession
    {
        public AgentController AgentController { get; }
        public AccessToken AccessToken { get; }
        public SessionRequestEx SessionRequestEx { get; }
        public SessionResponseEx SessionResponseEx { get; }

        public SampleSession(AgentController agentController, AccessToken accessToken, SessionRequestEx sessionRequestEx, SessionResponseEx sessionResponseEx)
        {
            AgentController = agentController;
            AccessToken = accessToken;
            SessionRequestEx = sessionRequestEx;
            SessionResponseEx = sessionResponseEx;
        }

        public Task<ResponseBase> AddUsage(long traffic)
        {
            return AgentController.UsageAsync(SessionResponseEx.SessionId, false,
                new UsageInfo { SentTraffic = traffic / 2, ReceivedTraffic = traffic / 2 });
        }

        public Task<ResponseBase> CloseSession()
        {
            return AgentController.UsageAsync(SessionResponseEx.SessionId, true,
                new UsageInfo ());
        }
    }

    public class SampleServer
    {
        public TestInit TestInit { get; }
        public AgentController AgentController { get; }
        public Api.Server Server { get; }
        public List<SampleSession> Sessions { get; } = new();
        public ServerInfo ServerInfo { get; }
        public ServerConfig ServerConfig { get; private set; } = default!;

        public SampleServer(TestInit testInit, AgentController agentController, Api.Server server, ServerInfo serverInfo)
        {
            TestInit = testInit;
            this.AgentController = agentController;
            Server = server;
            ServerInfo = serverInfo;
        }

        public static async Task<SampleServer> Create(TestInit testInit, Guid farmId)
        {
            var server = await testInit.ServerController.ServersPostAsync(testInit.ProjectId, new ServerCreateParams { AccessPointGroupId = farmId });
            var myServer = new SampleServer(
                testInit: testInit,
                agentController: testInit.CreateAgentController(server.ServerId),
                server: server,
                serverInfo: await testInit.NewServerInfo()
                );

            myServer.ServerConfig = await myServer.AgentController.ConfigureAsync(myServer.ServerInfo);
            await myServer.AgentController.StatusAsync(myServer.ServerInfo.Status);

            return myServer;
        }

        public async Task<SampleSession> AddSession(AccessToken accessToken, Guid? clientId = null)
        {
            var sessionRequestEx = TestInit.CreateSessionRequestEx(
                accessToken,
                clientId, 
                IPEndPoint.Parse(ServerConfig.TcpEndPoints.First()),
                await TestInit.NewIpV4());

            var testSession = new SampleSession(
                accessToken: accessToken,
                sessionRequestEx: sessionRequestEx,
                sessionResponseEx: await AgentController.SessionsPostAsync(sessionRequestEx),
                agentController: AgentController
                );

            Sessions.Add(testSession);
            return testSession;
        }
    }

    private SampleFarm(
        TestInit testInit,
        AccessPointGroup farm,
        SampleServer server1,
        SampleServer server2,
        AccessToken publicToken1,
        AccessToken publicToken2,
        AccessToken privateToken1,
        AccessToken privateToken2)
    {
        TestInit = testInit;
        Farm = farm;
        Server1 = server1;
        Server2 = server2;
        PublicToken1 = publicToken1;
        PublicToken2 = publicToken2;
        PrivateToken1 = privateToken1;
        PrivateToken2 = privateToken2;
    }

    public TestInit TestInit { get; }
    public AccessPointGroup Farm { get; }
    public SampleServer Server1 { get; }
    public SampleServer Server2 { get; }
    public AccessToken PublicToken1 { get; }
    public AccessToken PublicToken2 { get; }
    public AccessToken PrivateToken1 { get; }
    public AccessToken PrivateToken2 { get; }

    public static async Task<SampleFarm> Create(TestInit testInit)
    {
        var farm = await testInit.ServerFarmController.AccessPointGroupsPostAsync(testInit.ProjectId, new AccessPointGroupCreateParams());

        // create servers
        var sampleServers = new[]
        {
            await SampleServer.Create(testInit, farm.AccessPointGroupId),
            await SampleServer.Create(testInit, farm.AccessPointGroupId)
        };

        // create accessTokens
        var accessTokens = new[]
        {
            await testInit.AccessTokenController.AccessTokensPostAsync(testInit.ProjectId,
                new AccessTokenCreateParams {AccessPointGroupId = farm.AccessPointGroupId, IsPublic = true}),
            await testInit.AccessTokenController.AccessTokensPostAsync(testInit.ProjectId,
                new AccessTokenCreateParams {AccessPointGroupId = farm.AccessPointGroupId, IsPublic = true}),
            await testInit.AccessTokenController.AccessTokensPostAsync(testInit.ProjectId,
                new AccessTokenCreateParams {AccessPointGroupId = farm.AccessPointGroupId, IsPublic = false}),
            await testInit.AccessTokenController.AccessTokensPostAsync(testInit.ProjectId,
                new AccessTokenCreateParams {AccessPointGroupId = farm.AccessPointGroupId, IsPublic = false})
        };

        var sampleFarm = new SampleFarm(
            testInit: testInit,
            farm: farm,
            server1: sampleServers[0],
            server2: sampleServers[1],
            publicToken1: accessTokens[0],
            publicToken2: accessTokens[1],
            privateToken1: accessTokens[2],
            privateToken2: accessTokens[3]
            );

        // create 2 sessions per each token
        var sessionTasks1 = accessTokens.Select(x => sampleFarm.Server1.AddSession(x));
        var sessionTasks2 = accessTokens.Select(x => sampleFarm.Server2.AddSession(x));
        await Task.WhenAll(sessionTasks1.Concat(sessionTasks2));
        return sampleFarm;
    }
}

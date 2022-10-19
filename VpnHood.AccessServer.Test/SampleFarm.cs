using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VpnHood.AccessServer.Api;
using VpnHood.Common.Messaging;
using VpnHood.Server;
using VpnHood.Server.Messaging;

namespace VpnHood.AccessServer.Test;

public class SampleFarm
{
    public class SampleSession
    {
        public AgentClient AgentClient { get; }
        public AccessToken AccessToken { get; }
        public SessionRequestEx SessionRequestEx { get; }
        public SessionResponseEx SessionResponseEx { get; }

        public SampleSession(AgentClient agentClient, AccessToken accessToken, SessionRequestEx sessionRequestEx, SessionResponseEx sessionResponseEx)
        {
            AgentClient = agentClient;
            AccessToken = accessToken;
            SessionRequestEx = sessionRequestEx;
            SessionResponseEx = sessionResponseEx;
        }

        public Task<ResponseBase> AddUsage(long traffic)
        {
            return AgentClient.Session_AddUsage(SessionResponseEx.SessionId, new UsageInfo { SentTraffic = traffic / 2, ReceivedTraffic = traffic / 2 });
        }

        public Task<ResponseBase> CloseSession()
        {
            return AgentClient.Session_AddUsage(SessionResponseEx.SessionId, new UsageInfo(), true);
        }
    }

    public class SampleServer
    {
        public TestInit TestInit { get; }
        public AgentClient AgentClient { get; }
        public Api.Server Server { get; }
        public List<SampleSession> Sessions { get; } = new();
        public ServerInfo ServerInfo { get; }
        public ServerConfig ServerConfig { get; private set; } = default!;

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

            var testSession = new SampleSession(
                accessToken: accessToken,
                sessionRequestEx: sessionRequestEx,
                sessionResponseEx: await AgentClient.Session_Create(sessionRequestEx),
                agentClient: AgentClient
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
        var farm = await testInit.ServerFarmClient.CreateAsync(testInit.ProjectId, new AccessPointGroupCreateParams());

        // create servers
        var sampleServers = new[]
        {
            await SampleServer.Create(testInit, farm.AccessPointGroupId),
            await SampleServer.Create(testInit, farm.AccessPointGroupId)
        };

        // create accessTokens
        var accessTokens = new[]
        {
            await testInit.AccessTokenClient.CreateAsync(testInit.ProjectId,
                new AccessTokenCreateParams {AccessPointGroupId = farm.AccessPointGroupId, IsPublic = true}),
            await testInit.AccessTokenClient.CreateAsync(testInit.ProjectId,
                new AccessTokenCreateParams {AccessPointGroupId = farm.AccessPointGroupId, IsPublic = true}),
            await testInit.AccessTokenClient.CreateAsync(testInit.ProjectId,
                new AccessTokenCreateParams {AccessPointGroupId = farm.AccessPointGroupId, IsPublic = false}),
            await testInit.AccessTokenClient.CreateAsync(testInit.ProjectId,
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

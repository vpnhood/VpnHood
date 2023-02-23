using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using VpnHood.AccessServer.Api;
using VpnHood.Server;

namespace VpnHood.AccessServer.Test.Dom;

public class ServerDom
{
    public TestInit TestInit { get; }
    public AgentClient AgentClient { get; }
    public Api.Server Server { get; }
    public List<SessionDom> Sessions { get; } = new();
    public ServerInfo ServerInfo { get; }
    public Server.Configurations.ServerConfig ServerConfig { get; private set; } = default!;
    public Guid ServerId => Server.ServerId;

    public ServerDom(TestInit testInit, AgentClient agentClient, Api.Server server, ServerInfo serverInfo)
    {
        TestInit = testInit;
        AgentClient = agentClient;
        Server = server;
        ServerInfo = serverInfo;
    }

    public static async Task<ServerDom> Create(TestInit testInit, Guid accessPointGroupId, bool configure = true)
    {
        var server = await testInit.ServersClient.CreateAsync(testInit.ProjectId,
            new ServerCreateParams { AccessPointGroupId = accessPointGroupId});

        var myServer = new ServerDom(
            testInit: testInit,
            agentClient: testInit.CreateAgentClient(server.ServerId),
            server: server,
            serverInfo: await testInit.NewServerInfo()
        );

        if (configure)
            await myServer.Configure();

        return myServer;
    }

    public async Task Update(ServerUpdateParams updateParams)
    {
        await TestInit.ServersClient.UpdateAsync(TestInit.ProjectId, ServerId, updateParams);
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

    public async Task<SessionDom> AddSession(AccessToken accessToken, Guid? clientId = null)
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

    public async Task<AccessPoint> AddAccessPoint(Guid accessPointGroupId, IPAddress ipAddress, AccessPointMode mode = AccessPointMode.Private, bool isListen = false)
    {
        var ret = await AddAccessPoint(new AccessPointCreateParams
        {
            ServerId = ServerId,
            AccessPointGroupId = accessPointGroupId,
            IpAddress = ipAddress.ToString(),
            AccessPointMode = mode,
            IsListen = isListen
        });

        return ret;
    }

    public async Task<AccessPoint> AddAccessPoint(AccessPointCreateParams createParams)
    {
        if (createParams.ServerId != ServerId && createParams.ServerId != Guid.Empty)
            throw new ArgumentException("ServerId must be empty or the same as current server.");

        createParams.ServerId = createParams.ServerId;
        var ret = await TestInit.AccessPointsClient.CreateAsync(TestInit.ProjectId, createParams);
        return ret;
    }
}
using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.Common.Messaging;
using VpnHood.Server;
using VpnHood.Server.Messaging;

namespace VpnHood.AccessServer.Test.Dom;

public class SessionDom
{
    public TestInit TestInit { get; }
    public AgentClient AgentClient { get; }
    public AccessToken AccessToken { get; }
    public SessionRequestEx SessionRequestEx { get; }
    public SessionResponseEx SessionResponseEx { get; private set; }
    public long SessionId => SessionResponseEx.SessionId;

    private SessionDom(TestInit testInit, AgentClient agentClient, AccessToken accessToken, SessionRequestEx sessionRequestEx, SessionResponseEx sessionResponseEx)
    {
        TestInit = testInit;
        AgentClient = agentClient;
        AccessToken = accessToken;
        SessionRequestEx = sessionRequestEx;
        SessionResponseEx = sessionResponseEx;
    }

    public static async Task<SessionDom> Create(TestInit testInit, Guid serverId, AccessToken accessToken, SessionRequestEx sessionRequestEx, AgentClient? agentClient = null, bool assertError = true)
    {
        agentClient ??= testInit.CreateAgentClient(serverId);
        var sessionResponseEx = await agentClient.Session_Create(sessionRequestEx);
        if (assertError)
            Assert.AreEqual(SessionErrorCode.Ok, sessionResponseEx.ErrorCode, sessionResponseEx.ErrorMessage);
        var ret = new SessionDom(testInit, agentClient, accessToken, sessionRequestEx, sessionResponseEx);
        return ret;
    }

    public Task<SessionResponseBase> AddUsage(long traffic = 100)
    {
        return AddUsage(traffic/2, traffic/2);
    }

    public Task<SessionResponseBase> AddUsage(long sendTraffic, long receivedTraffic)
    {
        return AddUsage(new UsageInfo { SentTraffic = sendTraffic, ReceivedTraffic = receivedTraffic });
    }

    public Task<SessionResponseBase> AddUsage(UsageInfo usageInfo)
    {
        return AgentClient.Session_AddUsage(SessionResponseEx.SessionId, usageInfo);
    }

    public Task<SessionResponseBase> CloseSession()
    {
        return AgentClient.Session_Close(SessionResponseEx.SessionId, new UsageInfo());
    }

    public async Task Reload()
    {
        SessionResponseEx = await AgentClient.Session_Get((uint)SessionId, SessionRequestEx.HostEndPoint, SessionRequestEx.ClientIp);
    }

    public async Task<Dtos.Session> GetSessionFromCache()
    {
        return await TestInit.AgentCacheClient.GetSession(SessionId);
    }
}
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Dtos;
using VpnHood.Common.Messaging;
using VpnHood.Server.Access.Messaging;
using AccessToken = VpnHood.AccessServer.Api.AccessToken;

namespace VpnHood.AccessServer.Test.Dom;

public class SessionDom
{
    public TestInit TestInit { get; }
    public AgentClient AgentClient { get; }
    public AccessToken AccessToken { get; }
    public SessionRequestEx SessionRequestEx { get; }
    public SessionResponseEx SessionResponseEx { get; private set; }
    public long SessionId => (long)SessionResponseEx.SessionId;

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
        return AddUsage(new Traffic { Sent = sendTraffic, Received = receivedTraffic });
    }

    public Task<SessionResponseBase> AddUsage(Traffic traffic)
    {
        return AgentClient.Session_AddUsage(SessionResponseEx.SessionId, traffic);
    }

    public Task<SessionResponseBase> CloseSession()
    {
        return AgentClient.Session_Close(SessionResponseEx.SessionId, new Traffic());
    }

    public async Task Reload()
    {
        SessionResponseEx = await AgentClient.Session_Get((uint)SessionId, SessionRequestEx.HostEndPoint, SessionRequestEx.ClientIp);
    }

    public async Task<Session> GetSessionFromCache()
    {
        return await TestInit.AgentCacheClient.GetSession(SessionId);
    }
}
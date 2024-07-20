using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Persistence.Caches;
using VpnHood.Common.Messaging;
using VpnHood.Server.Access.Messaging;
using AccessToken = VpnHood.AccessServer.Api.AccessToken;

namespace VpnHood.AccessServer.Test.Dom;

public class SessionDom
{
    public TestApp TestApp { get; }
    public AgentClient AgentClient { get; }
    public AccessToken AccessToken { get; }
    public SessionRequestEx SessionRequestEx { get; }
    public SessionResponseEx SessionResponseEx { get; private set; }
    public long SessionId => (long)SessionResponseEx.SessionId;

    private SessionDom(TestApp testApp, AgentClient agentClient, AccessToken accessToken,
        SessionRequestEx sessionRequestEx, SessionResponseEx sessionResponseEx)
    {
        TestApp = testApp;
        AgentClient = agentClient;
        AccessToken = accessToken;
        SessionRequestEx = sessionRequestEx;
        SessionResponseEx = sessionResponseEx;
    }

    public static async Task<SessionDom> Create(TestApp testApp, Guid serverId, AccessToken accessToken,
        SessionRequestEx sessionRequestEx, AgentClient? agentClient = null, bool assertError = true)
    {
        agentClient ??= testApp.CreateAgentClient(serverId);
        var sessionResponseEx = await agentClient.Session_Create(sessionRequestEx);
        if (assertError)
            Assert.AreEqual(SessionErrorCode.Ok, sessionResponseEx.ErrorCode, sessionResponseEx.ErrorMessage);

        var ret = new SessionDom(testApp, agentClient, accessToken, sessionRequestEx, sessionResponseEx);
        return ret;
    }

    public Task<SessionResponse> AddUsage(long traffic = 100)
    {
        return AddUsage(traffic / 2, traffic / 2);
    }

    public Task<SessionResponse> AddUsage(long sendTraffic, long receivedTraffic)
    {
        return AddUsage(new Traffic { Sent = sendTraffic, Received = receivedTraffic });
    }

    public Task<SessionResponse> AddUsage(Traffic traffic, string? adData = null)
    {
        return AgentClient.Session_AddUsage(SessionResponseEx.SessionId, traffic, adData);
    }

    public Task<SessionResponse> CloseSession()
    {
        return AgentClient.Session_Close(SessionResponseEx.SessionId, new Traffic());
    }

    public async Task Reload()
    {
        SessionResponseEx =
            await AgentClient.Session_Get((uint)SessionId, SessionRequestEx.HostEndPoint, SessionRequestEx.ClientIp);
    }

    public Task<SessionCache> GetSessionFromCache()
    {
        return TestApp.AgentCacheClient.GetSession(SessionId);
    }
}
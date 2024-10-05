using VpnHood.AccessServer.Agent.Exceptions;
using VpnHood.AccessServer.Persistence.Caches;
using VpnHood.Common.Messaging;
using VpnHood.Server.Access.Messaging;
using AccessToken = VpnHood.AccessServer.Api.AccessToken;

namespace VpnHood.AccessServer.Test.Dom;

public class SessionDom
{
    private SessionCache? _sessionCache;
    public TestApp TestApp { get; }
    public AgentClient AgentClient { get; }
    public AccessToken AccessToken { get; }
    public SessionRequestEx SessionRequestEx { get; }
    public SessionResponseEx SessionResponseEx { get; private set; }
    public SessionCache SessionCache => _sessionCache ?? throw new SessionExceptionEx(SessionResponseEx);
    public long SessionId => (long)SessionResponseEx.SessionId;
    public Guid ServerId => SessionCache.ServerId;

    private SessionDom(TestApp testApp, AgentClient agentClient, AccessToken accessToken,
        SessionRequestEx sessionRequestEx, SessionResponseEx sessionResponseEx, SessionCache? sessionCache)
    {
        TestApp = testApp;
        AgentClient = agentClient;
        AccessToken = accessToken;
        SessionRequestEx = sessionRequestEx;
        SessionResponseEx = sessionResponseEx;
        _sessionCache = sessionCache;
    }

    public static async Task<SessionDom> Create(TestApp testApp, Guid serverId, AccessToken accessToken,
        SessionRequestEx sessionRequestEx, AgentClient? agentClient = null, bool throwError = true)
    {
        agentClient ??= testApp.CreateAgentClient(serverId);
        var sessionResponseEx = await agentClient.Session_Create(sessionRequestEx);
        if (throwError && sessionResponseEx.ErrorCode != SessionErrorCode.Ok)
            throw new SessionExceptionEx(sessionResponseEx);

        var sessionCache = sessionResponseEx.SessionId != 0 ? await testApp.AgentCacheClient.GetSession((long)sessionResponseEx.SessionId) : null;
        var ret = new SessionDom(testApp, agentClient, accessToken, sessionRequestEx, sessionResponseEx,
            sessionCache: sessionCache);
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
            await AgentClient.Session_Get((ulong)SessionId, SessionRequestEx.HostEndPoint, SessionRequestEx.ClientIp);

        _sessionCache = SessionId != 0 ? await TestApp.AgentCacheClient.GetSession(SessionId) : null;
    }
}
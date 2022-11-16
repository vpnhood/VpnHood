using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.Common.Messaging;
using VpnHood.Server;
using VpnHood.Server.Messaging;

namespace VpnHood.AccessServer.Test.Sampler;

public class SampleSession
{
    public TestInit TestInit { get; }
    public AgentClient AgentClient { get; }
    public AccessToken AccessToken { get; }
    public SessionRequestEx SessionRequestEx { get; }
    public SessionResponseEx SessionResponseEx { get; }
    public long SessionId => SessionResponseEx.SessionId;

    private SampleSession(TestInit testInit, AgentClient agentClient, AccessToken accessToken, SessionRequestEx sessionRequestEx, SessionResponseEx sessionResponseEx)
    {
        TestInit = testInit;
        AgentClient = agentClient;
        AccessToken = accessToken;
        SessionRequestEx = sessionRequestEx;
        SessionResponseEx = sessionResponseEx;
    }

    public static async Task<SampleSession> Create(TestInit testInit, Guid serverId, AccessToken accessToken, SessionRequestEx sessionRequestEx, AgentClient? agentClient = null, bool assertError = true)
    {
        agentClient ??= testInit.CreateAgentClient(serverId);
        var sessionResponseEx = await agentClient.Session_Create(sessionRequestEx);
        if (assertError)
            Assert.AreEqual(SessionErrorCode.Ok, sessionResponseEx.ErrorCode, sessionResponseEx.ErrorMessage);
        var ret = new SampleSession(testInit, agentClient, accessToken, sessionRequestEx, sessionResponseEx);
        return ret;
    }

    public Task<ResponseBase> AddUsage(long traffic)
    {
        return AddUsage(traffic/2, traffic/2);
    }

    public Task<ResponseBase> AddUsage(long sendTraffic, long receivedTraffic)
    {
        return AddUsage(new UsageInfo { SentTraffic = sendTraffic, ReceivedTraffic = receivedTraffic });
    }

    public Task<ResponseBase> AddUsage(UsageInfo usageInfo)
    {
        return AgentClient.Session_AddUsage(SessionResponseEx.SessionId, usageInfo);
    }

    public Task<ResponseBase> CloseSession()
    {
        return AgentClient.Session_AddUsage(SessionResponseEx.SessionId, new UsageInfo(), true);
    }

}
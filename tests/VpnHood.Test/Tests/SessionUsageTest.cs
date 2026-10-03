using System.Net;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Server;
using VpnHood.Core.Server.Access.Managers.FileAccessManagers;
using VpnHood.Core.Server.Access.Messaging;
using VpnHood.Net.Toolkit.ApiClients;
using VpnHood.Test.AccessManagers;
using VpnHood.Test.Device;
using VpnHood.Test.Extensions;

namespace VpnHood.Test.Tests;

[TestClass]
public class SessionUsageTest : TestBase
{
    [TestMethod]
    public async Task Usage_of_a_failed_request_goes_with_the_next()
    {
        using var accessManager = CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);
        var token = TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(vpnAdapter: new TestNullVpnAdapter(), token: token);
        var session = server.GetSession(client);

        // in maintenance, the access manager never gets the request: its usage goes with the next one
        AddTraffic(session, sent: 1000, received: 2000);
        accessManager.HttpAccessManagerServer.HttpExceptionStatusCode = HttpStatusCode.ServiceUnavailable;
        await Assert.ThrowsExactlyAsync<MaintenanceException>(() => server.SessionManager.Sync(true, TestCt));

        accessManager.HttpAccessManagerServer.HttpExceptionStatusCode = null;
        AddTraffic(session, sent: 3000, received: 4000);
        await server.SessionManager.Sync(true, TestCt);

        await AssertBilled(server, token, session);
    }

    [TestMethod]
    public async Task Usage_after_an_outage_goes_in_one_request()
    {
        using var accessManager = CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);
        var token = TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(vpnAdapter: new TestNullVpnAdapter(), token: token);
        var session = server.GetSession(client);

        // the requests of an outage fail
        accessManager.HttpAccessManagerServer.HttpExceptionStatusCode = HttpStatusCode.ServiceUnavailable;
        AddTraffic(session, sent: 1000, received: 2000);
        await Assert.ThrowsExactlyAsync<MaintenanceException>(() => server.SessionManager.Sync(true, TestCt));
        AddTraffic(session, sent: 3000, received: 4000);
        await Assert.ThrowsExactlyAsync<MaintenanceException>(() => server.SessionManager.Sync(true, TestCt));
        accessManager.HttpAccessManagerServer.HttpExceptionStatusCode = null;

        // back up, the access manager gets their usage merged, in one entry of one request
        var usage = await SendStatus(server, session);
        Assert.IsNotNull(usage);
        Assert.IsGreaterThanOrEqualTo(6000, usage.Sent);
        Assert.IsGreaterThanOrEqualTo(4000, usage.Received);

        await AssertBilled(server, token, session);
    }

    [TestMethod]
    public async Task Usage_of_a_lost_reply_goes_again()
    {
        using var accessManager = CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);
        var token = TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(vpnAdapter: new TestNullVpnAdapter(), token: token);
        var session = server.GetSession(client);
        await server.SessionManager.Sync(true, TestCt);

        // applied, then its reply lost: the server cannot tell, so it sends the usage again; it is billed twice
        AddTraffic(session, sent: 1000, received: 2000);
        accessManager.HttpAccessManagerServer.HttpExceptionStatusCodeAfterApply = HttpStatusCode.GatewayTimeout;
        await Assert.ThrowsExactlyAsync<ApiException>(() => server.SessionManager.Sync(true, TestCt));

        accessManager.HttpAccessManagerServer.HttpExceptionStatusCodeAfterApply = null;
        await server.SessionManager.Sync(true, TestCt);

        await AssertBilled(server, token, session, billedTwice: new Traffic(sent: 1000, received: 2000));
    }

    [TestMethod]
    public async Task Requests_go_one_at_a_time()
    {
        using var accessManager = CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);
        var token = TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(vpnAdapter: new TestNullVpnAdapter(), token: token);
        var session = server.GetSession(client);

        // a slow access manager holds a status upload
        var statusArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var statusRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        accessManager.HttpAccessManagerServer.StatusArrived = () => {
            statusArrived.TrySetResult();
            return statusRelease.Task;
        };

        AddTraffic(session, sent: 1000, received: 2000);
        var statusTask = server.ConfigureAndSendStatus(TestCt).AsTask();
        await statusArrived.Task.WaitAsync(TimeSpan.FromSeconds(30), TestCt);

        // a sync waits for it, then carries what came after
        AddTraffic(session, sent: 3000, received: 4000);
        var syncTask = server.SessionManager.Sync(true, TestCt);
        Assert.IsFalse(syncTask.IsCompleted);

        accessManager.HttpAccessManagerServer.StatusArrived = null;
        statusRelease.SetResult();
        await statusTask;
        await syncTask;

        await AssertBilled(server, token, session);
    }

    [TestMethod]
    public async Task Close_is_reported_once()
    {
        using var accessManager = CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);
        var token = TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(vpnAdapter: new TestNullVpnAdapter(), token: token);
        var session = server.GetSession(client);

        // the bye stops the session and reports its close at once
        await server.SessionManager.CloseSession(session.SessionId, TestCt);
        Assert.IsTrue(session.IsDisposed);
        Assert.AreEqual(SessionErrorCode.SessionClosed, GetAccessManagerErrorCode(accessManager, session));

        // nothing more is reported for it
        Assert.IsNull(await SendStatus(server, session));
    }

    [TestMethod]
    public async Task Close_by_the_access_manager_is_not_reported_back()
    {
        using var accessManager = CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);
        var token = TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(vpnAdapter: new TestNullVpnAdapter(), token: token);
        var session = server.GetSession(client);

        // the access manager closes the session in a reply
        AddTraffic(session, sent: 1000, received: 2000);
        server.SessionManager.ApplySessionResponses(new Dictionary<ulong, SessionResponse> {
            [session.SessionId] = new() { ErrorCode = SessionErrorCode.SessionSuppressedBy }
        });
        Assert.IsTrue(session.IsDisposed);

        // its last bytes go once, without its close
        var usage = await SendStatus(server, session);
        Assert.IsNotNull(usage);
        Assert.AreEqual(SessionErrorCode.Ok, usage.ErrorCode);
        Assert.IsGreaterThanOrEqualTo(2000, usage.Sent);
        Assert.IsGreaterThanOrEqualTo(1000, usage.Received);
        Assert.IsNull(await SendStatus(server, session));
    }

    [TestMethod]
    public async Task Close_is_reported_after_a_failed_sync_and_a_stale_reply()
    {
        using var accessManager = CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);
        var token = TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(vpnAdapter: new TestNullVpnAdapter(), token: token);
        var session = server.GetSession(client);

        // a slow access manager holds a status upload made while the session was open
        var statusArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var statusRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        accessManager.HttpAccessManagerServer.StatusArrived = () => {
            statusArrived.TrySetResult();
            return statusRelease.Task;
        };

        AddTraffic(session, sent: 1000, received: 2000);
        var statusTask = server.ConfigureAndSendStatus(TestCt).AsTask();
        await statusArrived.Task.WaitAsync(TimeSpan.FromSeconds(30), TestCt);

        // the client's bye waits for that upload; its own sync then fails
        var closeTask = server.SessionManager.CloseSession(session.SessionId, TestCt);
        accessManager.HttpAccessManagerServer.HttpExceptionStatusCode = HttpStatusCode.ServiceUnavailable;
        accessManager.HttpAccessManagerServer.StatusArrived = null;
        statusRelease.SetResult();
        await statusTask; // its reply still says the session is open
        await Assert.ThrowsExactlyAsync<MaintenanceException>(() => closeTask);
        accessManager.HttpAccessManagerServer.HttpExceptionStatusCode = null;

        // the close goes with the next status upload
        await server.ConfigureAndSendStatus(TestCt);
        Assert.AreEqual(SessionErrorCode.SessionClosed, GetAccessManagerErrorCode(accessManager, session));
    }

    [TestMethod]
    public async Task Close_after_a_failed_request_waits_for_the_status_upload()
    {
        using var accessManager = CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);
        var token = TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(vpnAdapter: new TestNullVpnAdapter(), token: token);
        var session = server.GetSession(client);

        // a request fails
        AddTraffic(session, sent: 1000, received: 2000);
        accessManager.HttpAccessManagerServer.HttpExceptionStatusCode = HttpStatusCode.ServiceUnavailable;
        await Assert.ThrowsExactlyAsync<MaintenanceException>(() => server.SessionManager.Sync(true, TestCt));
        accessManager.HttpAccessManagerServer.HttpExceptionStatusCode = null;

        // for a minute, a bye sends nothing: an access manager that may be down gets no request from every bye
        await server.SessionManager.CloseSession(session.SessionId, TestCt);
        Assert.AreEqual(SessionErrorCode.Ok, GetAccessManagerErrorCode(accessManager, session));

        await server.ConfigureAndSendStatus(TestCt);
        Assert.AreEqual(SessionErrorCode.SessionClosed, GetAccessManagerErrorCode(accessManager, session));
    }

    [TestMethod]
    public async Task Close_of_a_removed_session_still_goes()
    {
        using var accessManager = CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);
        var token = TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(vpnAdapter: new TestNullVpnAdapter(), token: token);
        var session = server.GetSession(client);

        // the bye's sync fails, and the session leaves memory before the next request
        accessManager.HttpAccessManagerServer.HttpExceptionStatusCode = HttpStatusCode.ServiceUnavailable;
        await Assert.ThrowsExactlyAsync<MaintenanceException>(() =>
            server.SessionManager.CloseSession(session.SessionId, TestCt));
        accessManager.HttpAccessManagerServer.HttpExceptionStatusCode = null;
        server.SessionManager.RemoveSession(session);

        // its close is not lost with it
        await server.ConfigureAndSendStatus(TestCt);
        Assert.AreEqual(SessionErrorCode.SessionClosed, GetAccessManagerErrorCode(accessManager, session));
    }

    [TestMethod]
    public async Task Close_made_while_a_sync_runs_goes_right_after_it()
    {
        using var accessManager = CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);
        await using var client1 = await TestHelper.CreateClient(vpnAdapter: new TestNullVpnAdapter(),
            token: TestHelper.CreateAccessToken(server));
        await using var client2 = await TestHelper.CreateClient(vpnAdapter: new TestNullVpnAdapter(),
            token: TestHelper.CreateAccessToken(server));
        var session1 = server.GetSession(client1);
        var session2 = server.GetSession(client2);

        // a slow access manager holds the sync of the first bye
        var syncArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var syncRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        accessManager.HttpAccessManagerServer.SyncArrived = () => {
            syncArrived.TrySetResult();
            return syncRelease.Task;
        };
        var closeTask1 = server.SessionManager.CloseSession(session1.SessionId, TestCt);
        await syncArrived.Task.WaitAsync(TimeSpan.FromSeconds(30), TestCt);

        // the second bye waits for it, then sends its close without waiting for a status upload
        var closeTask2 = server.SessionManager.CloseSession(session2.SessionId, TestCt);
        accessManager.HttpAccessManagerServer.SyncArrived = null;
        syncRelease.SetResult();
        await closeTask1;
        await closeTask2;

        Assert.AreEqual(SessionErrorCode.SessionClosed, GetAccessManagerErrorCode(accessManager, session2));
    }

    [TestMethod]
    public async Task Stop_reports_the_last_bytes_and_keeps_sessions_open()
    {
        using var accessManager = CreateAccessManager();
        var server = await TestHelper.CreateServer(accessManager);
        var token = TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(vpnAdapter: new TestNullVpnAdapter(), token: token);
        var session = server.GetSession(client);

        // the stop stops the sessions before its last sync, which carries all their bytes
        AddTraffic(session, sent: 1000, received: 2000);
        await server.DisposeAsync();
        await AssertTokenUsage(accessManager, token, session);

        // they stay open on the access manager, to recover after a restart
        Assert.AreEqual(SessionErrorCode.Ok, GetAccessManagerErrorCode(accessManager, session));
    }

    // only the test sends usage: the server's own status and sync wait an hour
    private TestHttpAccessManager CreateAccessManager()
    {
        var options = TestHelper.CreateFileAccessManagerOptions();
        options.UpdateStatusInterval = TimeSpan.FromHours(1);
        options.SessionOptions.SyncInterval = TimeSpan.FromHours(1);
        return TestHttpAccessManager.Create(TestHelper.CreateAccessManager(options));
    }

    private static void AddTraffic(Session session, long sent, long received)
    {
        session.Tunnel.TrafficMeter.OnSent(sent);
        session.Tunnel.TrafficMeter.OnReceived(received);
    }

    // the session's code as the access manager has it
    private static SessionErrorCode GetAccessManagerErrorCode(TestHttpAccessManager accessManager, Session session)
    {
        var fileAccessManager = (FileAccessManager)accessManager.HttpAccessManagerServer.BaseAccessManager;
        return fileAccessManager.SessionService.Sessions[session.SessionId].ErrorCode;
    }

    // a status upload, as the server's job sends one; returns what it reported for the session
    private async Task<SessionUsage?> SendStatus(VpnHoodServer server, Session session)
    {
        var accessManager = (TestHttpAccessManager)server.AccessManager;
        var baseAccessManager = (TestAccessManager)accessManager.HttpAccessManagerServer.BaseAccessManager;
        var lastServerStatus = baseAccessManager.LastServerStatus;
        await server.ConfigureAndSendStatus(TestCt);

        var serverStatus = baseAccessManager.LastServerStatus;
        Assert.IsNotNull(serverStatus);
        Assert.AreNotSame(lastServerStatus, serverStatus, "The status was not sent.");
        return serverStatus.SessionUsages.SingleOrDefault(x => x.SessionId == session.SessionId);
    }

    // stopped and its last bytes sent, the token's usage is the session's traffic, plus what was billed twice
    private async Task AssertBilled(VpnHoodServer server, Token token, Session session, Traffic? billedTwice = null)
    {
        session.Dispose();
        await server.SessionManager.Sync(true, TestCt);
        await AssertTokenUsage((TestHttpAccessManager)server.AccessManager, token, session, billedTwice);
    }

    private async Task AssertTokenUsage(TestHttpAccessManager accessManager, Token token, Session session,
        Traffic? billedTwice = null)
    {
        var fileAccessManager = (FileAccessManager)accessManager.HttpAccessManagerServer.BaseAccessManager;
        var accessTokenData = await fileAccessManager.AccessTokenService.Find(token.TokenId, TestCt);
        Assert.IsNotNull(accessTokenData);

        // the session counts the client's way: what the server received, the client has sent
        var traffic = session.Tunnel.TrafficMeter.Traffic + (billedTwice ?? new Traffic());
        Assert.AreEqual(traffic.Received, accessTokenData.Usage.Sent);
        Assert.AreEqual(traffic.Sent, accessTokenData.Usage.Received);
    }
}

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Client;
using VpnHood.Common;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Server.Providers.FileAccessServerProvider;

namespace VpnHood.Test.Tests;

[TestClass]
public class ServerTest
{
    [TestInitialize]
    public void Initialize()
    {
        VhLogger.Instance = VhLogger.CreateConsoleLogger(true);
    }

    [TestMethod]
    public async Task Auto_sync_sessions_by_interval()
    {
        // Create Server
        var serverOptions = TestHelper.CreateFileAccessServerOptions();
        serverOptions.SessionOptions.IcmpTimeout = TimeSpan.FromMilliseconds(1000);
        serverOptions.SessionOptions.SyncInterval = TimeSpan.FromMilliseconds(1000);
        serverOptions.SessionOptions.SyncCacheSize = 10000000;

        var fileAccessServer = TestHelper.CreateFileAccessServer(serverOptions);
        using var testAccessServer = new TestAccessServer(fileAccessServer);
        using var server = TestHelper.CreateServer(testAccessServer);
        var token = TestHelper.CreateAccessToken(server);

        // Create client
        using var client = TestHelper.CreateClient(token, options: new ClientOptions { UseUdpChannel = true });

        // check usage when usage should be 0
        var sessionResponseEx = await testAccessServer.Session_Get(client.SessionId, client.HostEndPoint!, null);
        Assert.IsTrue(sessionResponseEx.AccessUsage!.ReceivedTraffic == 0);

        // lets do transfer
        TestHelper.Test_Https();

        // check usage should still be 0 before interval
        sessionResponseEx = await testAccessServer.Session_Get(client.SessionId, client.HostEndPoint!, null);
        Assert.IsTrue(sessionResponseEx.AccessUsage!.ReceivedTraffic == 0);

        // check usage should still not be 0 after interval
        await Task.Delay(1500);
        sessionResponseEx = await testAccessServer.Session_Get(client.SessionId, client.HostEndPoint!, null);
        Assert.IsTrue(sessionResponseEx.AccessUsage!.ReceivedTraffic > 0);
    }

    [TestMethod]
    public async Task Reconfigure()
    {
        var serverEndPoint = Util.GetFreeEndPoint(IPAddress.Loopback);
        var fileAccessServerOptions = new FileAccessServerOptions { TcpEndPoints = new[] { serverEndPoint } };
        using var fileAccessServer = TestHelper.CreateFileAccessServer(fileAccessServerOptions);
        var serverConfig = fileAccessServer.ServerConfig;
        serverConfig.UpdateStatusInterval = TimeSpan.FromMilliseconds(500);
        serverConfig.TrackingOptions.TrackClientIp = true;
        serverConfig.TrackingOptions.TrackLocalPort = true;
        serverConfig.SessionOptions.TcpTimeout = TimeSpan.FromSeconds(2070);
        serverConfig.SessionOptions.UdpTimeout = TimeSpan.FromSeconds(2071);
        serverConfig.SessionOptions.IcmpTimeout = TimeSpan.FromSeconds(2072);
        serverConfig.SessionOptions.Timeout = TimeSpan.FromSeconds(2073);
        serverConfig.SessionOptions.MaxDatagramChannelCount = 2074;
        serverConfig.SessionOptions.SyncCacheSize = 2075;
        serverConfig.SessionOptions.TcpBufferSize = 2076;
        using var testAccessServer = new TestAccessServer(fileAccessServer);

        var dateTime = DateTime.Now;
        using var server = TestHelper.CreateServer(testAccessServer);
        Assert.IsTrue(testAccessServer.LastConfigureTime > dateTime);

        dateTime = DateTime.Now;
        fileAccessServer.ServerConfig.ConfigCode = Guid.NewGuid().ToString();
        for (var i = 0; i < 30 && fileAccessServer.ServerConfig.ConfigCode != testAccessServer.LastServerStatus!.ConfigCode; i++)
            await Task.Delay(100);

        Assert.AreEqual(fileAccessServer.ServerConfig.ConfigCode, testAccessServer.LastServerStatus!.ConfigCode);
        Assert.IsTrue(testAccessServer.LastConfigureTime > dateTime);
        Assert.IsTrue(server.SessionManager.TrackingOptions.TrackClientIp);
        Assert.IsTrue(server.SessionManager.TrackingOptions.TrackLocalPort);
        Assert.AreEqual(serverConfig.TrackingOptions.TrackClientIp, server.SessionManager.TrackingOptions.TrackClientIp);
        Assert.AreEqual(serverConfig.TrackingOptions.TrackLocalPort, server.SessionManager.TrackingOptions.TrackLocalPort);
        Assert.AreEqual(serverConfig.SessionOptions.TcpTimeout, server.SessionManager.SessionOptions.TcpTimeout);
        Assert.AreEqual(serverConfig.SessionOptions.IcmpTimeout, server.SessionManager.SessionOptions.IcmpTimeout);
        Assert.AreEqual(serverConfig.SessionOptions.UdpTimeout, server.SessionManager.SessionOptions.UdpTimeout);
        Assert.AreEqual(serverConfig.SessionOptions.Timeout, server.SessionManager.SessionOptions.Timeout);
        Assert.AreEqual(serverConfig.SessionOptions.MaxDatagramChannelCount, server.SessionManager.SessionOptions.MaxDatagramChannelCount);
        Assert.AreEqual(serverConfig.SessionOptions.SyncCacheSize, server.SessionManager.SessionOptions.SyncCacheSize);
        Assert.AreEqual(serverConfig.SessionOptions.TcpBufferSize, server.SessionManager.SessionOptions.TcpBufferSize);
    }

    [TestMethod]
    public void Close_session_by_client_disconnect()
    {
        // create server
        using var fileAccessServer = TestHelper.CreateFileAccessServer();
        using var testAccessServer = new TestAccessServer(fileAccessServer);
        using var server = TestHelper.CreateServer(testAccessServer);

        // create client
        var token = TestHelper.CreateAccessToken(server);
        using var client = TestHelper.CreateClient(token);
        Assert.IsTrue(fileAccessServer.SessionManager.Sessions.TryGetValue(client.SessionId, out var session));
        client.Dispose();
        TestHelper.WaitForClientState(client, ClientState.Disposed);
        Thread.Sleep(1000);

        Assert.IsFalse(session.IsAlive);
    }

    [TestMethod]
    public void Recover_closed_session_from_access_server()
    {
        // create server
        using var fileAccessServer = TestHelper.CreateFileAccessServer();
        using var testAccessServer = new TestAccessServer(fileAccessServer);
        using var server = TestHelper.CreateServer(testAccessServer);

        // create client
        var token = TestHelper.CreateAccessToken(server);
        using var client = TestHelper.CreateClient(token);
        Assert.AreEqual(ClientState.Connected, client.State);
        TestHelper.Test_Https();

        // restart server
        server.Dispose();

        using var server2 = TestHelper.CreateServer(testAccessServer);
        TestHelper.Test_Https();
        Assert.AreEqual(ClientState.Connected, client.State);
    }

    [TestMethod]
    public async Task Server_should_close_session_if_it_does_not_exist_in_access_server()
    {
        // create server
        var accessServerOptions = TestHelper.CreateFileAccessServerOptions();
        accessServerOptions.SessionOptions.SyncCacheSize = 1000000;
        using var fileAccessServer = TestHelper.CreateFileAccessServer(accessServerOptions);
        using var testAccessServer = new TestAccessServer(fileAccessServer);
        using var server = TestHelper.CreateServer(testAccessServer);

        // create client
        var token = TestHelper.CreateAccessToken(server);
        using var client = TestHelper.CreateClient(token);

        fileAccessServer.SessionManager.Sessions.Clear();
        TestHelper.Test_Https();
        await server.SessionManager.SyncSessions();

        try
        {
            TestHelper.Test_Https();
            Assert.Fail("Must fail. SessionOptions does not exist any more.");
        }
        catch { /*ignored*/ }

        TestHelper.WaitForClientState(client, ClientState.Disposed);
        Assert.AreEqual(SessionErrorCode.AccessError, client.SessionStatus.ErrorCode);
    }
}
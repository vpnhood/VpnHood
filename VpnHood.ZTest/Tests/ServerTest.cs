using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using VpnHood.Client;
using VpnHood.Common;
using VpnHood.Common.Logging;
using VpnHood.Server.AccessServers;

namespace VpnHood.Test.Tests
{
    [TestClass]
    public class ServerTest
    {
        [TestInitialize]
        public void Initialize()
        {
            VhLogger.Instance = VhLogger.CreateConsoleLogger(true);
        }

        [TestMethod]
        public async Task Reconfigure()
        {
            var serverEndPoint = Util.GetFreeEndPoint(IPAddress.Loopback);
            var fileAccessServerOptions = new FileAccessServerOptions { TcpEndPoints = new[] { serverEndPoint } };
            using var fileAccessServer = TestHelper.CreateFileAccessServer(fileAccessServerOptions);
            var serverConfig = fileAccessServer.ServerConfig;
            serverConfig.UpdateStatusInterval = TimeSpan.FromSeconds(1);
            serverConfig.TrackingOptions.LogClientIp = true;
            serverConfig.TrackingOptions.LogLocalPort = true;
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
            testAccessServer.ConfigCode = Guid.NewGuid();
            await Task.Delay(2500);
            Assert.IsNull(testAccessServer.ConfigCode);
            Assert.IsTrue(testAccessServer.LastConfigureTime > dateTime);
            Assert.IsTrue(server.SessionManager.TrackingOptions.LogClientIp);
            Assert.IsTrue(server.SessionManager.TrackingOptions.LogLocalPort);
            Assert.AreEqual(serverConfig.TrackingOptions.LogClientIp, server.SessionManager.TrackingOptions.LogClientIp);
            Assert.AreEqual(serverConfig.TrackingOptions.LogLocalPort, server.SessionManager.TrackingOptions.LogLocalPort);
            Assert.AreEqual(serverConfig.SessionOptions.TcpTimeout, server.SessionManager.SessionOptions.TcpTimeout);
            Assert.AreEqual(serverConfig.SessionOptions.IcmpTimeout, server.SessionManager.SessionOptions.IcmpTimeout);
            Assert.AreEqual(serverConfig.SessionOptions.UdpTimeout, server.SessionManager.SessionOptions.UdpTimeout);
            Assert.AreEqual(serverConfig.SessionOptions.Timeout, server.SessionManager.SessionOptions.Timeout);
            Assert.AreEqual(serverConfig.SessionOptions.SyncCacheSize, server.SessionManager.SessionOptions.SyncCacheSize);
            Assert.AreEqual(serverConfig.SessionOptions.MaxDatagramChannelCount, server.SessionManager.SessionOptions.MaxDatagramChannelCount);
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

            Assert.IsFalse(session!.IsAlive);
        }

    }
}
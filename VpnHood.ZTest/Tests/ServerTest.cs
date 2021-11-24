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
            fileAccessServer.ServerConfig.UpdateStatusInterval = 1;
            using var testAccessServer = new TestAccessServer(fileAccessServer);

            var dateTime = DateTime.Now;
            using var server = TestHelper.CreateServer(testAccessServer);
            Assert.IsTrue(testAccessServer.LastConfigureTime > dateTime);

            dateTime = DateTime.Now;
            testAccessServer.ConfigCode = Guid.NewGuid();
            await Task.Delay(2500);
            Assert.IsNull(testAccessServer.ConfigCode);
            Assert.IsTrue(testAccessServer.LastConfigureTime > dateTime);
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
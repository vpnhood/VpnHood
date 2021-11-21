using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Net;
using System.Threading.Tasks;
using VpnHood.Common;
using VpnHood.Server.AccessServers;

namespace VpnHood.Test.Tests
{
    [TestClass]
    public class ServerTest
    {
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
            await Task.Delay(2000);
            Assert.IsNull(testAccessServer.ConfigCode);
            Assert.IsTrue(testAccessServer.LastConfigureTime > dateTime);
        }
    }
}
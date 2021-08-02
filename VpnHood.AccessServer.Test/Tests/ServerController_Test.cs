using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading.Tasks;
using VpnHood.AccessServer.Controllers;
using VpnHood.AccessServer.Models;
using VpnHood.Server;

namespace VpnHood.AccessServer.Test
{
    [TestClass]
    public class ServerController_Test : ControllerTest
    {
        [TestMethod]
        public async Task Subscribe()
        {
            var dateTime = DateTime.Now;

            // create serverInfo
            AccessController accessController = TestInit.CreateAccessController();
            ServerInfo serverInfo = new()
            {
                Version = Version.Parse("1.2.3.4"),
                EnvironmentVersion = Environment.Version,
                OsVersion = Environment.OSVersion.ToString(),
                FreeMemory = 10,
                LocalIp = TestInit.ServerEndPoint_New1.Address,
                PublicIp = TestInit.ServerEndPoint_New2.Address,
                MachineName = Guid.NewGuid().ToString(),
                TotalMemory = 20,
            };

            //Subscribe
            var serverId = Guid.NewGuid();
            await accessController.Subscribe(serverId, serverInfo);

            var serverController = TestInit.CreateServerController();
            var serverData = await serverController.Get(TestInit.AccountId_1, serverId);
            var server = serverData.Server;
            var serverStatusLog = serverData.Status;

            Assert.AreEqual(serverId, server.ServerId);
            Assert.AreEqual(serverInfo.Version, Version.Parse(server.Version));
            Assert.AreEqual(serverInfo.EnvironmentVersion, Version.Parse(server.EnvironmentVersion));
            Assert.AreEqual(serverInfo.OsVersion, server.OsVersion);
            Assert.AreEqual(serverInfo.MachineName, server.MachineName);
            Assert.AreEqual(serverInfo.TotalMemory, server.TotalMemory);
            Assert.AreEqual(serverInfo.LocalIp, server.LocalIp);
            Assert.AreEqual(serverInfo.PublicIp, server.PublicIp);
            Assert.IsTrue(dateTime <= server.SubscribeTime);
            Assert.IsTrue(dateTime <= server.CreatedTime);

            Assert.AreEqual(serverId, serverStatusLog.ServerId);
            Assert.AreEqual(0, serverStatusLog.FreeMemory);
            Assert.AreEqual(true, serverStatusLog.IsSubscribe);
            Assert.AreEqual(0, serverStatusLog.NatTcpCount);
            Assert.AreEqual(0, serverStatusLog.NatUdpCount);
            Assert.AreEqual(0, serverStatusLog.SessionCount);
            Assert.AreEqual(0, serverStatusLog.ThreadCount);
            Assert.AreEqual(true, serverStatusLog.IsLast);
            Assert.IsTrue(dateTime <= serverStatusLog.CreatedTime);

            //-----------
            // check: SubscribeLog is inserted
            //-----------
            Models.ServerStatusLog[] statusLogs = await serverController.GetStatusLogs(TestInit.AccountId_1, serverId, recordCount: 100);
            var statusLog = statusLogs[0];
            
            // check with serverData
            Assert.AreEqual(serverStatusLog.ServerId, statusLog.ServerId);
            Assert.AreEqual(serverStatusLog.FreeMemory, statusLog.FreeMemory);
            Assert.AreEqual(serverStatusLog.IsSubscribe, statusLog.IsSubscribe);
            Assert.AreEqual(serverStatusLog.NatTcpCount, statusLog.NatTcpCount);
            Assert.AreEqual(serverStatusLog.NatUdpCount, statusLog.NatUdpCount);
            Assert.AreEqual(serverStatusLog.SessionCount, statusLog.SessionCount);
            Assert.AreEqual(serverStatusLog.ThreadCount, statusLog.ThreadCount);
            Assert.AreEqual(serverStatusLog.IsLast, statusLog.IsLast);
            Assert.IsTrue(dateTime <= statusLog.CreatedTime);


            //-----------
            // check: Check ServerStatus log is inserted
            //-----------
            Random random = new();
            var serverStatus = new Server.ServerStatus()
            {
                FreeMemory = random.Next(0, 0xFFFF),
                NatTcpCount = random.Next(0, 0xFFFF),
                NatUdpCount = random.Next(0, 0xFFFF),
                SessionCount = random.Next(0, 0xFFFF),
                ThreadCount = random.Next(0, 0xFFFF),
            };

            dateTime = DateTime.Now;
            await Task.Delay(500);
            await accessController.SendServerStatus(serverId, serverStatus);
            statusLogs = await serverController.GetStatusLogs(TestInit.AccountId_1, serverId, recordCount: 100);
            statusLog = statusLogs[0];
            Assert.AreEqual(serverId, statusLog.ServerId);
            Assert.AreEqual(serverStatus.FreeMemory, statusLog.FreeMemory);
            Assert.AreEqual(false, statusLog.IsSubscribe);
            Assert.AreEqual(serverStatus.NatTcpCount, statusLog.NatTcpCount);
            Assert.AreEqual(serverStatus.NatUdpCount, statusLog.NatUdpCount);
            Assert.AreEqual(serverStatus.SessionCount, statusLog.SessionCount);
            Assert.AreEqual(serverStatus.ThreadCount, statusLog.ThreadCount);
            Assert.IsTrue(statusLog.IsLast);
            Assert.IsTrue(statusLog.CreatedTime > dateTime);

            //-----------
            // check: Update
            //-----------
            dateTime = DateTime.Now;
            await Task.Delay(500);
            serverInfo.MachineName = $"Machine-{Guid.NewGuid()}";
            serverInfo.Version = Version.Parse("1.2.3.5");
            await accessController.Subscribe(serverId, serverInfo);
            serverData = await serverController.Get(TestInit.AccountId_1, serverId);
            Assert.AreEqual(serverInfo.MachineName, serverData.Server.MachineName);
            Assert.IsTrue(dateTime > serverData.Server.CreatedTime );
            Assert.IsTrue(dateTime < serverData.Server.SubscribeTime);
            Assert.IsTrue(dateTime < serverData.Status.CreatedTime);
        }
    }
}

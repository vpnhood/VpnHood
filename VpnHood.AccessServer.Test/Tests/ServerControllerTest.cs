using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Controllers;
using VpnHood.AccessServer.DTOs;
using VpnHood.AccessServer.Models;
using VpnHood.Server;

namespace VpnHood.AccessServer.Test.Tests
{
    [TestClass]
    public class ServerControllerTest : ControllerTest
    {
        [TestMethod]
        public async Task Subscribe()
        {
            var dateTime = DateTime.UtcNow;

            // create serverInfo
            AccessController accessController = TestInit1.CreateAccessController();
            ServerInfo serverInfo = new(Version.Parse("1.2.3.4"))
            {
                EnvironmentVersion = Environment.Version,
                OsInfo = Environment.OSVersion.ToString(),
                LocalIp = await TestInit.NewIp(),
                PublicIp = await TestInit.NewIp(),
                MachineName = Guid.NewGuid().ToString(),
                TotalMemory = 20,
            };

            //Subscribe
            var serverId = Guid.NewGuid();
            await accessController.ServerSubscribe(serverId, serverInfo);

            var serverController = TestInit1.CreateServerController();
            var serverData = await serverController.Get(TestInit1.ProjectId, serverId);
            var server = serverData.Server;
            var serverStatusLog = serverData.Status;

            Assert.AreEqual(serverId, server.ServerId);
            Assert.AreEqual(serverInfo.Version, Version.Parse(server.Version!));
            Assert.AreEqual(serverInfo.EnvironmentVersion, Version.Parse(server.EnvironmentVersion ?? "0.0.0"));
            Assert.AreEqual(serverInfo.OsInfo, server.OsInfo);
            Assert.AreEqual(serverInfo.MachineName, server.MachineName);
            Assert.AreEqual(serverInfo.TotalMemory, server.TotalMemory);
            Assert.AreEqual(serverInfo.LocalIp, server.LocalIp);
            Assert.AreEqual(serverInfo.PublicIp, server.PublicIp);
            Assert.IsTrue(dateTime <= server.SubscribeTime);
            Assert.IsTrue(dateTime <= server.CreatedTime);
            Assert.IsNotNull(serverStatusLog);

            Assert.AreEqual(serverId, serverStatusLog.ServerId);
            Assert.AreEqual(0, serverStatusLog.FreeMemory);
            Assert.AreEqual(true, serverStatusLog.IsSubscribe);
            Assert.AreEqual(0, serverStatusLog.TcpConnectionCount);
            Assert.AreEqual(0, serverStatusLog.UdpConnectionCount);
            Assert.AreEqual(0, serverStatusLog.SessionCount);
            Assert.AreEqual(0, serverStatusLog.ThreadCount);
            Assert.AreEqual(true, serverStatusLog.IsLast);
            Assert.IsTrue(dateTime <= serverStatusLog.CreatedTime);

            //-----------
            // check: SubscribeLog is inserted
            //-----------
            ServerStatusLog[] statusLogs =
                await serverController.GetStatusLogs(TestInit1.ProjectId, serverId, recordCount: 100);
            var statusLog = statusLogs[0];

            // check with serverData
            Assert.AreEqual(serverStatusLog.ServerId, statusLog.ServerId);
            Assert.AreEqual(serverStatusLog.FreeMemory, statusLog.FreeMemory);
            Assert.AreEqual(serverStatusLog.IsSubscribe, statusLog.IsSubscribe);
            Assert.AreEqual(serverStatusLog.TcpConnectionCount, statusLog.TcpConnectionCount);
            Assert.AreEqual(serverStatusLog.UdpConnectionCount, statusLog.UdpConnectionCount);
            Assert.AreEqual(serverStatusLog.SessionCount, statusLog.SessionCount);
            Assert.AreEqual(serverStatusLog.ThreadCount, statusLog.ThreadCount);
            Assert.AreEqual(serverStatusLog.IsLast, statusLog.IsLast);
            Assert.IsTrue(dateTime <= statusLog.CreatedTime);


            //-----------
            // check: Check ServerStatus log is inserted
            //-----------
            Random random = new();
            var serverStatus = new ServerStatus
            {
                FreeMemory = random.Next(0, 0xFFFF),
                TcpConnectionCount = random.Next(0, 0xFFFF),
                UdpConnectionCount = random.Next(0, 0xFFFF),
                SessionCount = random.Next(0, 0xFFFF),
                ThreadCount = random.Next(0, 0xFFFF),
            };

            dateTime = DateTime.UtcNow;
            await Task.Delay(500);
            await accessController.SendServerStatus(serverId, serverStatus);
            statusLogs = await serverController.GetStatusLogs(TestInit1.ProjectId, serverId, recordCount: 100);
            statusLog = statusLogs[0];
            Assert.AreEqual(serverId, statusLog.ServerId);
            Assert.AreEqual(serverStatus.FreeMemory, statusLog.FreeMemory);
            Assert.AreEqual(false, statusLog.IsSubscribe);
            Assert.AreEqual(serverStatus.TcpConnectionCount, statusLog.TcpConnectionCount);
            Assert.AreEqual(serverStatus.UdpConnectionCount, statusLog.UdpConnectionCount);
            Assert.AreEqual(serverStatus.SessionCount, statusLog.SessionCount);
            Assert.AreEqual(serverStatus.ThreadCount, statusLog.ThreadCount);
            Assert.IsTrue(statusLog.IsLast);
            Assert.IsTrue(statusLog.CreatedTime > dateTime);

            //-----------
            // check: Update
            //-----------
            dateTime = DateTime.UtcNow;
            await Task.Delay(500);
            serverInfo.MachineName = $"Machine-{Guid.NewGuid()}";
            serverInfo.Version = Version.Parse("1.2.3.5");
            await accessController.ServerSubscribe(serverId, serverInfo);
            serverData = await serverController.Get(TestInit1.ProjectId, serverId);
            Assert.AreEqual(serverInfo.MachineName, serverData.Server.MachineName);
            Assert.IsNotNull(serverData.Status);
            Assert.IsTrue(dateTime > serverData.Server.CreatedTime);
            Assert.IsTrue(dateTime < serverData.Server.SubscribeTime);
            Assert.IsTrue(dateTime < serverData.Status.CreatedTime);
        }

        [TestMethod]
        public async Task Crud()
        {
            //-----------
            // check: Update
            //-----------
            var serverController = TestInit1.CreateServerController();
            var server1ACreateParam = new ServerCreateParams { ServerName = $"{Guid.NewGuid()}" };
            var server1A = await serverController.Create(TestInit1.ProjectId, server1ACreateParam);

            //-----------
            // check: Get
            //-----------
            var server1B = await serverController.Get(TestInit1.ProjectId, server1A.ServerId);
            Assert.AreEqual(server1ACreateParam.ServerName, server1B.Server.ServerName);

            //-----------
            // check: List
            //-----------
            var servers = await serverController.List(TestInit1.ProjectId);
            Assert.IsTrue(servers.Any(x => x.Server.ServerName == server1ACreateParam.ServerName && x.Server.ServerId==server1A.ServerId));
        }
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Net;
using System.Threading.Tasks;
using VpnHood.AccessServer.Controllers;
using VpnHood.AccessServer.Models;
using VpnHood.Server;

namespace VpnHood.AccessServer.Test
{
    [TestClass]
    public class ClientController_Test : ControllerTest
    {
        [TestMethod]
        public async Task ClientId_is_unique_per_project()
        {
            var clientId = Guid.NewGuid();

            ClientIdentity clientIdentity1 = new ClientIdentity {
                ClientId = clientId,
                TokenId = TestInit1.AccessTokenId_1,
                ClientVersion = "1.1.1",
                UserAgent = "ClientR1",
                ClientIp = IPAddress.Parse("1.1.1.1")
            };

            ClientIdentity clientIdentity2 = new ClientIdentity
            {
                ClientId = clientId,
                TokenId = TestInit2.AccessTokenId_1,
                ClientVersion = "1.1.2",
                UserAgent = "ClientR2",
                ClientIp = IPAddress.Parse("1.1.1.2")
            };

            var accessController1 = TestInit1.CreateAccessController();
            var accessController2 = TestInit2.CreateAccessController();
            await accessController1.GetAccess(TestInit1.ServerId_1, new AccessParams { ClientIdentity = clientIdentity1, RequestEndPoint = TestInit1.ServerEndPoint_G1S1 });
            await accessController2.GetAccess(TestInit2.ServerId_1, new AccessParams { ClientIdentity = clientIdentity2, RequestEndPoint = TestInit2.ServerEndPoint_G1S1 });

            var clientController = TestInit.CreateClientController();
            
            var client1 = await clientController.Get(TestInit1.ProjectId, clientIdentity1.ClientId);
            Assert.AreEqual(client1.ClientId, clientIdentity1.ClientId);
            Assert.AreEqual(client1.ClientVersion, clientIdentity1.ClientVersion);
            Assert.AreEqual(client1.UserAgent, clientIdentity1.UserAgent);

            var client2 = await clientController.Get(TestInit2.ProjectId, clientIdentity2.ClientId);
            Assert.AreEqual(client2.ClientId, clientIdentity2.ClientId);
            Assert.AreEqual(client2.ClientVersion, clientIdentity2.ClientVersion);
            Assert.AreEqual(client2.UserAgent, clientIdentity2.UserAgent);

            Assert.AreNotEqual(client1.ClientKeyId, client2.ClientKeyId);
        }

    }

    [TestClass]
    public class ServerController_Test : ControllerTest
    {
        [TestMethod]
        public async Task Subscribe()
        {
            var dateTime = DateTime.Now;

            // create serverInfo
            AccessController accessController = TestInit1.CreateAccessController();
            ServerInfo serverInfo = new()
            {
                Version = Version.Parse("1.2.3.4"),
                EnvironmentVersion = Environment.Version,
                OsVersion = Environment.OSVersion.ToString(),
                FreeMemory = 10,
                LocalIp = await TestInit.NewIp(),
                PublicIp = await TestInit.NewIp(),
                MachineName = Guid.NewGuid().ToString(),
                TotalMemory = 20,
            };

            //Subscribe
            var serverId = Guid.NewGuid();
            await accessController.Subscribe(serverId, serverInfo);

            var serverController = TestInit.CreateServerController();
            var serverData = await serverController.Get(TestInit1.ProjectId, serverId);
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
            Models.ServerStatusLog[] statusLogs = await serverController.GetStatusLogs(TestInit1.ProjectId, serverId, recordCount: 100);
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
            statusLogs = await serverController.GetStatusLogs(TestInit1.ProjectId, serverId, recordCount: 100);
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
            serverData = await serverController.Get(TestInit1.ProjectId, serverId);
            Assert.AreEqual(serverInfo.MachineName, serverData.Server.MachineName);
            Assert.IsTrue(dateTime > serverData.Server.CreatedTime );
            Assert.IsTrue(dateTime < serverData.Server.SubscribeTime);
            Assert.IsTrue(dateTime < serverData.Status.CreatedTime);
        }
    }
}

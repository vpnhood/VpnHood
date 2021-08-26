using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Net;
using System.Threading.Tasks;
using VpnHood.AccessServer.Controllers;
using VpnHood.Common.Messaging;
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
            var accessRequest1 = TestInit1.CreateSessionRequestEx(clientIp: IPAddress.Parse("1.1.1.1"));
            accessRequest1.ClientInfo = new ClientInfo
            {
                ClientId = clientId,
                ClientVersion = "1.1.1",
                UserAgent = "ClientR1",
            };

            var accessRequest2 = TestInit2.CreateSessionRequestEx(clientIp: IPAddress.Parse("1.1.1.2"));
            accessRequest2.ClientInfo = new ClientInfo
            {
                ClientId = clientId,
                ClientVersion = "1.1.2",
                UserAgent = "ClientR2"
            };

            var accessController1 = TestInit1.CreateAccessController();
            var accessController2 = TestInit2.CreateAccessController();
            await accessController1.Session_Create(TestInit1.ServerId_1, accessRequest1);
            await accessController2.Session_Create(TestInit2.ServerId_1, accessRequest2);

            var clientController = TestInit.CreateClientController();
            
            var client1 = await clientController.Get(TestInit1.ProjectId, clientId);
            Assert.AreEqual(client1.UserClientId, accessRequest1.ClientInfo.ClientId);
            Assert.AreEqual(client1.ClientVersion, accessRequest1.ClientInfo.ClientVersion);
            Assert.AreEqual(client1.UserAgent, accessRequest1.ClientInfo.UserAgent);

            var client2 = await clientController.Get(TestInit2.ProjectId, clientId);
            Assert.AreEqual(client2.UserClientId, accessRequest2.ClientInfo.ClientId);
            Assert.AreEqual(client2.ClientVersion, accessRequest2.ClientInfo.ClientVersion);
            Assert.AreEqual(client2.UserAgent, accessRequest2.ClientInfo.UserAgent);

            Assert.AreNotEqual(client1.ClientId, client2.ClientId);
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

            var serverController = TestInit.CreateServerController();
            var serverData = await serverController.Get(TestInit1.ProjectId, serverId);
            var server = serverData.Server;
            var serverStatusLog = serverData.Status;

            Assert.AreEqual(serverId, server.ServerId);
            Assert.AreEqual(serverInfo.Version, Version.Parse(server.Version));
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
            Models.ServerStatusLog[] statusLogs = await serverController.GetStatusLogs(TestInit1.ProjectId, serverId, recordCount: 100);
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
            var serverStatus = new Server.ServerStatus()
            {
                FreeMemory = random.Next(0, 0xFFFF),
                TcpConnectionCount = random.Next(0, 0xFFFF),
                UdpConnectionCount = random.Next(0, 0xFFFF),
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
            Assert.AreEqual(serverStatus.TcpConnectionCount, statusLog.TcpConnectionCount);
            Assert.AreEqual(serverStatus.UdpConnectionCount, statusLog.UdpConnectionCount);
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
            await accessController.ServerSubscribe(serverId, serverInfo);
            serverData = await serverController.Get(TestInit1.ProjectId, serverId);
            Assert.AreEqual(serverInfo.MachineName, serverData.Server.MachineName);
            Assert.IsNotNull(serverData.Status);
            Assert.IsTrue(dateTime > serverData.Server.CreatedTime );
            Assert.IsTrue(dateTime < serverData.Server.SubscribeTime);
            Assert.IsTrue(dateTime < serverData.Status.CreatedTime);
        }
    }
}

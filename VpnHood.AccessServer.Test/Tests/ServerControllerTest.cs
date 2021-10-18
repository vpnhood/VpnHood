using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Controllers;
using VpnHood.AccessServer.DTOs;
using VpnHood.AccessServer.Models;
using VpnHood.Common;
using ServerStatusLog = VpnHood.AccessServer.Models.ServerStatusLog;

namespace VpnHood.AccessServer.Test.Tests
{
    [TestClass]
    public class ServerControllerTest : ControllerTest
    {
        [TestMethod]
        public async Task Configure()
        {
            // create serverInfo
            var serverController = TestInit1.CreateServerController();
            var serverId = (await serverController.Create(TestInit1.ProjectId, new ServerCreateParams { AccessPointGroupId = TestInit1.AccessPointGroupId1 })) .ServerId;
            var dateTime = DateTime.UtcNow;

            // create serverInfo
            var agentController1 = TestInit1.CreateAgentController(serverId);
            var serverInfo1 = await TestInit.NewServerInfo();
            var publicIp = await TestInit.NewIpV6();
            serverInfo1.PrivateIpAddresses = new[] { publicIp, await TestInit.NewIpV4(), await TestInit.NewIpV6() };
            serverInfo1.PublicIpAddresses = new[] { publicIp, await TestInit.NewIpV4(), await TestInit.NewIpV6() };

            //Configure
            await agentController1.ServerConfigure(serverInfo1);

            var serverData = await serverController.Get(TestInit1.ProjectId, serverId);
            var server = serverData.Server;
            var serverStatusLog = serverData.Status;

            Assert.AreEqual(serverId, server.ServerId);
            Assert.AreEqual(serverInfo1.Version, Version.Parse(server.Version!));
            Assert.AreEqual(serverInfo1.EnvironmentVersion, Version.Parse(server.EnvironmentVersion ?? "0.0.0"));
            Assert.AreEqual(serverInfo1.OsInfo, server.OsInfo);
            Assert.AreEqual(serverInfo1.MachineName, server.MachineName);
            Assert.AreEqual(serverInfo1.TotalMemory, server.TotalMemory);
            Assert.IsTrue(dateTime <= server.ConfigureTime);
            Assert.IsNotNull(serverStatusLog);

            Assert.AreEqual(server.ServerId, serverStatusLog.ServerId);
            Assert.AreEqual(serverInfo1.Status.FreeMemory, serverStatusLog.FreeMemory);
            Assert.IsTrue(serverStatusLog.IsConfigure);
            Assert.AreEqual(serverInfo1.Status.TcpConnectionCount, serverStatusLog.TcpConnectionCount);
            Assert.AreEqual(serverInfo1.Status.UdpConnectionCount, serverStatusLog.UdpConnectionCount);
            Assert.AreEqual(serverInfo1.Status.SessionCount, serverStatusLog.SessionCount);
            Assert.AreEqual(serverInfo1.Status.ThreadCount, serverStatusLog.ThreadCount);
            Assert.IsTrue(serverStatusLog.IsLast);
            Assert.IsTrue(dateTime <= serverStatusLog.CreatedTime);

            //-----------
            // check: ConfigureLog is inserted
            //-----------
            var statusLogs = await serverController.GetStatusLogs(TestInit1.ProjectId, server.ServerId, recordCount: 100);
            var statusLog = statusLogs[0];

            // check with serverData
            Assert.AreEqual(serverStatusLog.ServerId, statusLog.ServerId);
            Assert.AreEqual(serverStatusLog.FreeMemory, statusLog.FreeMemory);
            Assert.AreEqual(serverStatusLog.IsConfigure, statusLog.IsConfigure);
            Assert.AreEqual(serverStatusLog.TcpConnectionCount, statusLog.TcpConnectionCount);
            Assert.AreEqual(serverStatusLog.UdpConnectionCount, statusLog.UdpConnectionCount);
            Assert.AreEqual(serverStatusLog.SessionCount, statusLog.SessionCount);
            Assert.AreEqual(serverStatusLog.ThreadCount, statusLog.ThreadCount);
            Assert.AreEqual(serverStatusLog.IsLast, statusLog.IsLast);
            Assert.IsTrue(dateTime <= statusLog.CreatedTime);

            //-----------
            // check: Check ServerStatus log is inserted
            //-----------
            var serverStatus = TestInit.NewServerStatus();

            dateTime = DateTime.UtcNow;
            await Task.Delay(500);
            await agentController1.UpdateServerStatus(serverStatus);
            statusLogs = await serverController.GetStatusLogs(TestInit1.ProjectId, server.ServerId, recordCount: 100);
            statusLog = statusLogs[0];
            Assert.AreEqual(server.ServerId, statusLog.ServerId);
            Assert.AreEqual(serverStatus.FreeMemory, statusLog.FreeMemory);
            Assert.AreEqual(false, statusLog.IsConfigure);
            Assert.AreEqual(serverStatus.TcpConnectionCount, statusLog.TcpConnectionCount);
            Assert.AreEqual(serverStatus.UdpConnectionCount, statusLog.UdpConnectionCount);
            Assert.AreEqual(serverStatus.SessionCount, statusLog.SessionCount);
            Assert.AreEqual(serverStatus.ThreadCount, statusLog.ThreadCount);
            Assert.IsTrue(statusLog.IsLast);
            Assert.IsTrue(statusLog.CreatedTime > dateTime);
        }

        [TestMethod]
        public async Task Configure_auto_update_accessPoints_on()
        {
            // create serverInfo
            var accessPointGroupController = TestInit1.CreateAccessPointGroupController();

            var accessPointGroup1 = await accessPointGroupController.Create(TestInit1.ProjectId, new AccessPointGroupCreateParams());
            var serverController = TestInit1.CreateServerController();
            var server = await serverController.Create(TestInit1.ProjectId, new ServerCreateParams { AccessPointGroupId = accessPointGroup1.AccessPointGroupId });
            
            var publicInTokenAccessPoint1 = await Configure_auto_update_accessPoints_on_internal(server);
            var publicInTokenAccessPoint2 = await Configure_auto_update_accessPoints_on_internal(server);

            // --------
            // Check: The only PublicInToken should be changed by second configure
            // --------
            Assert.IsNotNull(publicInTokenAccessPoint1);
            Assert.IsNotNull(publicInTokenAccessPoint2);
            Assert.AreNotEqual(publicInTokenAccessPoint1.IpAddress, publicInTokenAccessPoint2.IpAddress);

            // --------
            // Check: another server with same group should not have any PublicInTokenAccess
            // --------
            server = await serverController.Create(TestInit1.ProjectId, new ServerCreateParams { AccessPointGroupId = accessPointGroup1.AccessPointGroupId });
            var publicInTokenAccessPoint = await Configure_auto_update_accessPoints_on_internal(server);
            Assert.IsNull(publicInTokenAccessPoint);

            // another server with different group should one PublicInTokenAccess
            var accessPointGroup2 = await accessPointGroupController.Create(TestInit1.ProjectId, new AccessPointGroupCreateParams());
            server = await serverController.Create(TestInit1.ProjectId, new ServerCreateParams { AccessPointGroupId = accessPointGroup2.AccessPointGroupId });
            publicInTokenAccessPoint = await Configure_auto_update_accessPoints_on_internal(server);
            Assert.IsNotNull(publicInTokenAccessPoint);
        }

        // return the only PublicInToken AccessPoint
        public async Task<AccessPoint?> Configure_auto_update_accessPoints_on_internal(Models.Server server)
        {
            var accessPointController = TestInit1.CreateAccessPointController();

            // create serverInfo
            var serverInfo = await TestInit.NewServerInfo();
            var publicIp = await TestInit.NewIpV6();
            serverInfo.PrivateIpAddresses = new[] { publicIp, await TestInit.NewIpV4(), await TestInit.NewIpV6() };
            serverInfo.PublicIpAddresses = new[] { publicIp, await TestInit.NewIpV4(), await TestInit.NewIpV6() };

            //Configure
            var agentController = TestInit1.CreateAgentController(server.ServerId);
            await agentController.ServerConfigure(serverInfo);

            //-----------
            // check: Configure with AutoUpdate is true (Server.AccessPointGroupId is set)
            //-----------
            var accessPoints = await accessPointController.List(TestInit1.ProjectId, server.ServerId);
            var totalServerInfoIpAddress = serverInfo.PrivateIpAddresses.Concat(serverInfo.PublicIpAddresses).Distinct().Count();
            Assert.AreEqual(totalServerInfoIpAddress, accessPoints.Length); 

            // private[0]
            var accessPoint = accessPoints.Single(x => x.IpAddress == serverInfo.PrivateIpAddresses[0].ToString());
            Assert.IsTrue(accessPoint.AccessPointMode is AccessPointMode.Public or AccessPointMode.PublicInToken, "shared publicIp and privateIp must be see as publicIp");
            Assert.AreEqual(443, accessPoint.TcpPort);
            Assert.AreEqual(0, accessPoint.UdpPort);
            Assert.AreEqual(server.AccessPointGroupId, accessPoint.AccessPointGroupId);
            Assert.IsTrue(accessPoint.IsListen, "shared publicIp and privateIp");

            // private[1]
            accessPoint = accessPoints.Single(x => x.IpAddress == serverInfo.PrivateIpAddresses[1].ToString());
            Assert.AreEqual(AccessPointMode.Private, accessPoint.AccessPointMode);
            Assert.AreEqual(443, accessPoint.TcpPort);
            Assert.AreEqual(0, accessPoint.UdpPort);
            Assert.AreEqual(server.AccessPointGroupId, accessPoint.AccessPointGroupId);
            Assert.IsTrue(accessPoint.IsListen);

            // private[2]
            accessPoint = accessPoints.Single(x => x.IpAddress == serverInfo.PrivateIpAddresses[2].ToString());
            Assert.AreEqual(AccessPointMode.Private, accessPoint.AccessPointMode);
            Assert.AreEqual(443, accessPoint.TcpPort);
            Assert.AreEqual(0, accessPoint.UdpPort);
            Assert.AreEqual(server.AccessPointGroupId, accessPoint.AccessPointGroupId);
            Assert.IsTrue(accessPoint.IsListen);

            // public[0]
            accessPoint = accessPoints.Single(x => x.IpAddress == serverInfo.PublicIpAddresses[0].ToString());
            Assert.IsTrue(accessPoint.AccessPointMode is AccessPointMode.Public or AccessPointMode.PublicInToken);
            Assert.AreEqual(443, accessPoint.TcpPort);
            Assert.AreEqual(0, accessPoint.UdpPort);
            Assert.AreEqual(server.AccessPointGroupId, accessPoint.AccessPointGroupId);
            Assert.IsTrue(accessPoint.IsListen, "shared publicIp and privateIp");

            // public[1]
            accessPoint = accessPoints.Single(x => x.IpAddress == serverInfo.PublicIpAddresses[1].ToString());
            Assert.IsTrue(accessPoint.AccessPointMode is AccessPointMode.Public or AccessPointMode.PublicInToken);
            Assert.AreEqual(443, accessPoint.TcpPort);
            Assert.AreEqual(0, accessPoint.UdpPort);
            Assert.AreEqual(server.AccessPointGroupId, accessPoint.AccessPointGroupId);
            Assert.IsFalse(accessPoint.IsListen);

            // public[2]
            accessPoint = accessPoints.Single(x => x.IpAddress == serverInfo.PublicIpAddresses[2].ToString());
            Assert.IsTrue(accessPoint.AccessPointMode is AccessPointMode.Public or AccessPointMode.PublicInToken);
            Assert.AreEqual(443, accessPoint.TcpPort);
            Assert.AreEqual(0, accessPoint.UdpPort);
            Assert.AreEqual(server.AccessPointGroupId, accessPoint.AccessPointGroupId);
            Assert.IsFalse(accessPoint.IsListen);

            // PublicInToken should never be deleted
            return accessPoints.SingleOrDefault(x => x.AccessPointMode == AccessPointMode.PublicInToken);
        }

        [TestMethod]
        public async Task Configure_auto_update_accessPoints_off()
        {
            // create serverInfo
            var serverController = TestInit1.CreateServerController();
            var server = await serverController.Create(TestInit1.ProjectId, new ServerCreateParams { AccessPointGroupId = null });

            var accessPointController = TestInit1.CreateAccessPointController();
            var accessPoint1 = await accessPointController.Create(server.ProjectId, server.ServerId, 
                new AccessPointCreateParams(await TestInit.NewIpV4())
                {
                    AccessPointGroupId = TestInit1.AccessPointGroupId1,
                    AccessPointMode = AccessPointMode.PublicInToken,
                    IsListen = true,
                    TcpPort = 4848,
                    UdpPort = 150
                });

            var accessPoint2 = await accessPointController.Create(server.ProjectId, server.ServerId,
                new AccessPointCreateParams(await TestInit.NewIpV4())
                {
                    AccessPointGroupId = TestInit1.AccessPointGroupId1,
                    AccessPointMode = AccessPointMode.Private,
                    IsListen = true,
                    TcpPort = 5010,
                    UdpPort = 0
                });

            var serverInfo1 = await TestInit.NewServerInfo();
            var publicIp = await TestInit.NewIpV6();
            serverInfo1.PrivateIpAddresses = new[] { publicIp, await TestInit.NewIpV4(), await TestInit.NewIpV6() };
            serverInfo1.PublicIpAddresses = new[] { publicIp, await TestInit.NewIpV4(), await TestInit.NewIpV6() };
            
            // Configure
            var agentController1 = TestInit1.CreateAgentController(server.ServerId);
            await agentController1.ServerConfigure(serverInfo1);

            // Test that accessPoints have not been changed
            var accessPoints = await accessPointController.List(TestInit1.ProjectId, server.ServerId);
            Assert.AreEqual(2, accessPoints.Length);
            
            // AccessPoint1
            var expectedAccessPoint = accessPoint1;
            var actualAccessPoint = accessPoints.Single(x => x.IpAddress == expectedAccessPoint.IpAddress);
            Assert.AreEqual(expectedAccessPoint.AccessPointMode, actualAccessPoint.AccessPointMode);
            Assert.AreEqual(expectedAccessPoint.TcpPort, actualAccessPoint.TcpPort);
            Assert.AreEqual(expectedAccessPoint.UdpPort, actualAccessPoint.UdpPort);
            Assert.AreEqual(expectedAccessPoint.AccessPointGroupId, actualAccessPoint.AccessPointGroupId);
            Assert.AreEqual(expectedAccessPoint.IsListen, actualAccessPoint.IsListen);

            // AccessPoint2
            expectedAccessPoint = accessPoint2;
            actualAccessPoint = accessPoints.Single(x => x.IpAddress == expectedAccessPoint.IpAddress);
            Assert.AreEqual(expectedAccessPoint.AccessPointMode, actualAccessPoint.AccessPointMode);
            Assert.AreEqual(expectedAccessPoint.TcpPort, actualAccessPoint.TcpPort);
            Assert.AreEqual(expectedAccessPoint.UdpPort, actualAccessPoint.UdpPort);
            Assert.AreEqual(expectedAccessPoint.AccessPointGroupId, actualAccessPoint.AccessPointGroupId);
            Assert.AreEqual(expectedAccessPoint.IsListen, actualAccessPoint.IsListen);
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
            Assert.AreEqual(0, server1A.Secret.Length);

            //-----------
            // check: Get
            //-----------
            var server1B = await serverController.Get(TestInit1.ProjectId, server1A.ServerId);
            Assert.AreEqual(server1ACreateParam.ServerName, server1B.Server.ServerName);
            Assert.AreEqual(0, server1B.Server.Secret.Length);

            //-----------
            // check: List
            //-----------
            var servers = await serverController.List(TestInit1.ProjectId);
            Assert.IsTrue(servers.Any(x => x.Server.ServerName == server1ACreateParam.ServerName && x.Server.ServerId == server1A.ServerId));
            Assert.IsTrue(servers.All(x => x.Server.Secret.Length == 0));
        }

        [TestMethod]
        public async Task GetAppSettingsJson()
        {
            var serverController = TestInit1.CreateServerController();
            var json = await serverController.GetAppSettingsJson(TestInit1.ProjectId, TestInit1.ServerId1);
            var agentAppSettings = Util.JsonDeserialize<AgentAppSettings>(json);
            Assert.IsFalse(Util.IsNullOrEmpty(agentAppSettings.RestSecret));
            Assert.IsFalse(string.IsNullOrEmpty(agentAppSettings.RestAuthorization));
            Assert.IsNotNull(agentAppSettings.RestBaseUrl);
        }
    }
}

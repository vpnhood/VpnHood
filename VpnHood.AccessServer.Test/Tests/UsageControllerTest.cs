using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.DTOs;
using VpnHood.AccessServer.Models;
using VpnHood.Server;

namespace VpnHood.AccessServer.Test.Tests
{
    [TestClass]
    public class UsageControllerTest : ControllerTest
    {
        [TestMethod]
        public async Task GetUsageSummary()
        {
            var fillData = await TestInit2.Fill();

            var projectController = TestInit2.CreateProjectController();
            var res = await projectController.GetUsageSummary(TestInit2.ProjectId);
            Assert.AreEqual(fillData.SessionRequests.Count, res.DeviceCount);
        }

        [TestMethod]
        public async Task LiveUsageSummary()
        {
            var serverController = TestInit2.CreateServerController();
            var projectController = TestInit2.CreateProjectController();

            var agentController = TestInit2.CreateAgentController(TestInit2.ServerId1);
            await agentController.UpdateServerStatus(new ServerStatus { SessionCount = 1, TunnelReceiveSpeed = 100, TunnelSendSpeed = 50 });

            agentController = TestInit2.CreateAgentController(TestInit2.ServerId2);
            await agentController.UpdateServerStatus(new ServerStatus { SessionCount = 2, TunnelReceiveSpeed = 300, TunnelSendSpeed = 200 });

            // notInstalled 4
            await serverController.Create(TestInit2.ProjectId, new ServerCreateParams());
            await serverController.Create(TestInit2.ProjectId, new ServerCreateParams());
            await serverController.Create(TestInit2.ProjectId, new ServerCreateParams());
            await serverController.Create(TestInit2.ProjectId, new ServerCreateParams());

            // idle1
            var server = await serverController.Create(TestInit2.ProjectId, new ServerCreateParams());
            agentController = TestInit2.CreateAgentController(server.ServerId);
            await agentController.ConfigureServer(await TestInit.NewServerInfo());
            await agentController.UpdateServerStatus(new ServerStatus { SessionCount = 0 });

            // idle2
            server = await serverController.Create(TestInit2.ProjectId, new ServerCreateParams());
            agentController = TestInit2.CreateAgentController(server.ServerId);
            await agentController.ConfigureServer(await TestInit.NewServerInfo());
            await agentController.UpdateServerStatus(new ServerStatus { SessionCount = 0 });

            // idle3
            server = await serverController.Create(TestInit2.ProjectId, new ServerCreateParams());
            agentController = TestInit2.CreateAgentController(server.ServerId);
            await agentController.ConfigureServer(await TestInit.NewServerInfo());
            await agentController.UpdateServerStatus(new ServerStatus { SessionCount = 0 });

            // lost
            server = await serverController.Create(TestInit2.ProjectId, new ServerCreateParams());
            agentController = TestInit2.CreateAgentController(server.ServerId);
            await agentController.ConfigureServer(await TestInit.NewServerInfo());
            await agentController.UpdateServerStatus(new ServerStatus { SessionCount = 10 });
            await using var vhContext = new VhContext();
            var serverStatus = vhContext.ServerStatus.First(x => x.ServerId == server.ServerId && x.IsLast);
            serverStatus.CreatedTime = DateTime.UtcNow - TimeSpan.FromMinutes(20);
            vhContext.ServerStatus.Update(serverStatus);
            await vhContext.SaveChangesAsync();

            var liveUsageSummary = await projectController.GeLiveUsageSummary(TestInit2.ProjectId);
            Assert.AreEqual(10, liveUsageSummary.TotalServerCount);
            Assert.AreEqual(2, liveUsageSummary.ActiveServerCount);
            Assert.AreEqual(4, liveUsageSummary.NotInstalledServerCount);
            Assert.AreEqual(1, liveUsageSummary.LostServerCount);
            Assert.AreEqual(3, liveUsageSummary.IdleServerCount);
            Assert.AreEqual(250, liveUsageSummary.TunnelSendSpeed);
            Assert.AreEqual(400, liveUsageSummary.TunnelReceiveSpeed);
        }
    }
}
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VpnHood.AccessServer.Test.Tests
{
    [TestClass]
    public class DeviceControllerTest : ControllerTest
    {
        [TestMethod]
        public async Task ClientId_is_unique_per_project()
        {
            var clientId = Guid.NewGuid();
            var sessionRequestEx1 = TestInit1.CreateSessionRequestEx(clientId: clientId, clientIp: IPAddress.Parse("1.1.1.1"));
            sessionRequestEx1.ClientInfo.UserAgent = "ClientR1";
            sessionRequestEx1.ClientInfo.ClientVersion = "1.1.1";

            var sessionRequestEx2 = TestInit2.CreateSessionRequestEx(clientId: clientId, clientIp: IPAddress.Parse("1.1.1.2"));
            sessionRequestEx2.ClientInfo.UserAgent = "ClientR2";
            sessionRequestEx2.ClientInfo.ClientVersion = "1.1.2";

            var agentController1 = TestInit1.CreateAgentController();
            var agentController2 = TestInit2.CreateAgentController();
            await agentController1.Session_Create(sessionRequestEx1);
            await agentController2.Session_Create(sessionRequestEx2);

            var deviceController1 = TestInit1.CreateDeviceController();

            var device1 = await deviceController1.FindByClientId(TestInit1.ProjectId, clientId);
            Assert.AreEqual(device1.ClientId, sessionRequestEx1.ClientInfo.ClientId);
            Assert.AreEqual(device1.ClientVersion, sessionRequestEx1.ClientInfo.ClientVersion);
            Assert.AreEqual(device1.UserAgent, sessionRequestEx1.ClientInfo.UserAgent);

            var deviceController2 = TestInit2.CreateDeviceController();
            var device2 = await deviceController2.FindByClientId(TestInit2.ProjectId, clientId);
            Assert.AreEqual(device2.ClientId, sessionRequestEx2.ClientInfo.ClientId);
            Assert.AreEqual(device2.ClientVersion, sessionRequestEx2.ClientInfo.ClientVersion);
            Assert.AreEqual(device2.UserAgent, sessionRequestEx2.ClientInfo.UserAgent);

            Assert.AreNotEqual(device1.DeviceId, device2.DeviceId);
        }

        [TestMethod]
        public async Task List()
        {
            var fillData = await TestInit2.Fill();
            var deviceController = TestInit2.CreateDeviceController();
            var res = await deviceController.List(TestInit2.ProjectId);
            Assert.AreEqual(fillData.SessionRequests.Count, res.Length);
        }

    }
}
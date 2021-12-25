using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.DTOs;
using VpnHood.Common.Messaging;

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

            var sessionRequestEx2 = TestInit2.CreateSessionRequestEx(clientId: clientId, clientIp: IPAddress.Parse("1.1.1.2"));
            sessionRequestEx2.ClientInfo.UserAgent = "ClientR2";

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
        public async Task Search()
        {
            var fillData = await TestInit2.Fill();
            var deviceController = TestInit2.CreateDeviceController();
            var res = await deviceController.Search(TestInit2.ProjectId);
            Assert.AreEqual(fillData.SessionRequests.Count, res.Length);

            var res1 = await deviceController.Search(TestInit2.ProjectId, fillData.SessionRequests[0].ClientInfo.ClientId.ToString());
            Assert.AreEqual(1, res1.Length);

        }

        [TestMethod]
        public async Task Locked()
        {
            var clientId = Guid.NewGuid();
            var sessionRequestEx = TestInit1.CreateSessionRequestEx(clientId: clientId, clientIp: IPAddress.Parse("1.1.1.1"));

            var agentController = TestInit1.CreateAgentController();
            await agentController.Session_Create(sessionRequestEx);

            var deviceController = TestInit1.CreateDeviceController();
            var device = await deviceController.FindByClientId(TestInit1.ProjectId, clientId);
            Assert.IsNull(device.LockedTime);

            await deviceController.Update(TestInit1.ProjectId, device.DeviceId, new DeviceUpdateParams { IsLocked = false });
            device = (await deviceController.Get(TestInit1.ProjectId, device.DeviceId)).Device;
            Assert.IsNull(device.LockedTime);

            await deviceController.Update(TestInit1.ProjectId, device.DeviceId, new DeviceUpdateParams { IsLocked = true });
            device = (await deviceController.Get(TestInit1.ProjectId, device.DeviceId)).Device;
            Assert.IsTrue(device.LockedTime > TestInit1.CreatedTime);

            // check access
            var sessionResponseEx = await agentController.Session_Create(sessionRequestEx);
            Assert.AreEqual(SessionErrorCode.AccessLocked, sessionResponseEx.ErrorCode);

            await deviceController.Update(TestInit1.ProjectId, device.DeviceId, new DeviceUpdateParams { IsLocked = false });
            device = (await deviceController.Get(TestInit1.ProjectId, device.DeviceId)).Device;
            Assert.IsNull(device.LockedTime);
            sessionResponseEx = await agentController.Session_Create(sessionRequestEx);
            Assert.AreEqual(SessionErrorCode.Ok, sessionResponseEx.ErrorCode);

        }

    }
}
using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VpnHood.AccessServer.Test.Tests
{
    [TestClass]
    public class ClientControllerTest : ControllerTest
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

            var clientController1 = TestInit1.CreateClientController();

            var client1 = await clientController1.Get(TestInit1.ProjectId, clientId);
            Assert.AreEqual(client1.ClientId, sessionRequestEx1.ClientInfo.ClientId);
            Assert.AreEqual(client1.ClientVersion, sessionRequestEx1.ClientInfo.ClientVersion);
            Assert.AreEqual(client1.UserAgent, sessionRequestEx1.ClientInfo.UserAgent);

            var clientController2 = TestInit2.CreateClientController();
            var client2 = await clientController2.Get(TestInit2.ProjectId, clientId);
            Assert.AreEqual(client2.ClientId, sessionRequestEx2.ClientInfo.ClientId);
            Assert.AreEqual(client2.ClientVersion, sessionRequestEx2.ClientInfo.ClientVersion);
            Assert.AreEqual(client2.UserAgent, sessionRequestEx2.ClientInfo.UserAgent);

            Assert.AreNotEqual(client1.ProjectClientId, client2.ProjectClientId);
        }

        [TestMethod]
        public async Task List()
        {
            await TestInit1.Fill();
            await TestInit1.Fill();
            await TestInit1.Fill();
            await TestInit1.Fill();
            await TestInit1.Fill();


            var fillData = await TestInit2.Fill();
            var clientController = TestInit2.CreateClientController();
            var res = await clientController.List(TestInit2.ProjectId);
            Assert.AreEqual(fillData.SessionRequests.Count, res.Length);
        }

    }
}
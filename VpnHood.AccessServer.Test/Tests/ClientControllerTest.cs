using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Common.Messaging;

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

            var accessController1 = TestInit1.CreateAccessController();
            var accessController2 = TestInit2.CreateAccessController();
            await accessController1.Session_Create(TestInit1.ServerId1, sessionRequestEx1);
            await accessController2.Session_Create(TestInit2.ServerId1, sessionRequestEx2);

            var clientController = TestInit.CreateClientController();

            var client1 = await clientController.Get(TestInit1.ProjectId, clientId);
            Assert.AreEqual(client1.ClientId, sessionRequestEx1.ClientInfo.ClientId);
            Assert.AreEqual(client1.ClientVersion, sessionRequestEx1.ClientInfo.ClientVersion);
            Assert.AreEqual(client1.UserAgent, sessionRequestEx1.ClientInfo.UserAgent);

            var client2 = await clientController.Get(TestInit2.ProjectId, clientId);
            Assert.AreEqual(client2.ClientId, sessionRequestEx2.ClientInfo.ClientId);
            Assert.AreEqual(client2.ClientVersion, sessionRequestEx2.ClientInfo.ClientVersion);
            Assert.AreEqual(client2.UserAgent, sessionRequestEx2.ClientInfo.UserAgent);

            Assert.AreNotEqual(client1.ProjectClientId, client2.ProjectClientId);
        }
    }
}
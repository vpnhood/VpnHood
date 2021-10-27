using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.DTOs;
using VpnHood.Common;

namespace VpnHood.AccessServer.Test.Tests
{
    [TestClass]
    public class ServerControllerTest : ControllerTest
    {
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
            Assert.IsTrue(servers.Any(x =>
                x.Server.ServerName == server1ACreateParam.ServerName && x.Server.ServerId == server1A.ServerId));
            Assert.IsTrue(servers.All(x => x.Server.Secret.Length == 0));
        }

        [TestMethod]
        public async Task ServerInstallManuall()
        {
            var serverController = TestInit1.CreateServerController();
            var serverInstall = await serverController.InstallByManual(TestInit1.ProjectId, TestInit1.ServerId1);
            Assert.IsFalse(Util.IsNullOrEmpty(serverInstall.AppSettings.Secret));
            Assert.IsFalse(string.IsNullOrEmpty(serverInstall.AppSettings.RestAccessServer.Authorization));
            Assert.IsNotNull(serverInstall.AppSettings.RestAccessServer.BaseUrl);
            Assert.IsNotNull(serverInstall.LinuxCommand);
        }

        [TestMethod]
        public async Task ServerInstallByUserNAme()
        {
            var serverController = TestInit1.CreateServerController();
            try
            {
                await serverController.InstallBySshUserPassword(TestInit1.ProjectId, TestInit1.ServerId1,
                        new ServerInstallBySshUserPasswordParams("127.0.0.1", "user", "pass"));
            }
            catch (SocketException)
            {
                // ignore
            }

            try
            {
                await serverController.InstallBySshUserKey(TestInit1.ProjectId, TestInit1.ServerId1,
                    new ServerInstallBySshUserKeyParams("127.0.0.1", "user", TestResource.test_ssh_key));
            }
            catch (SocketException)
            {
                // ignore
            }
        }
    }
}

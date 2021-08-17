using VpnHood.Client.App;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using VpnHood.Client;
using VpnHood.Common;
using EmbedIO;
using System.Text;
using VpnHood.Server.AccessServers;
using VpnHood.Client.Device;
using System.Net;
using System.Threading;

namespace VpnHood.Test
{
    [TestClass]
    public class Test_ClientApp
    {
        private int _lastSupportId;
        private Token CreateToken()
        {
            var randomId = Guid.NewGuid();
            return new Token(secret: randomId.ToByteArray(),
                             certificateHash: randomId.ToByteArray(),
                             serverAuthority: randomId.ToString())
            {
                Name = "Default Test Server",
                SupportId = _lastSupportId++,
                ServerEndPoint = IPEndPoint.Parse("127.0.0.1:443"),
                TokenId = randomId
            };
        }

        [TestMethod]
        public void Add_remove_clientProfiles()
        {
            using var app = TestHelper.CreateClientApp();

            // ************
            // *** TEST ***: AddAccessKey should add a clientProfile
            var clientProfiles = app.ClientProfileStore.ClientProfiles;
            var token1 = CreateToken();
            var clientProfile1 = app.ClientProfileStore.AddAccessKey(token1.ToAccessKey());
            Assert.AreEqual(1, app.ClientProfileStore.ClientProfiles.Count(x => x.TokenId == token1.TokenId), "ClientProfile is not added");
            Assert.AreEqual(token1.TokenId, clientProfile1.TokenId, "invalid tokenId has been assigned to clientProfile");

            // ************
            // *** TEST ***: AddAccessKey with new accessKey should add another clientProfile
            var token2 = CreateToken();
            app.ClientProfileStore.AddAccessKey(token2.ToAccessKey());
            Assert.AreEqual(1, app.ClientProfileStore.ClientProfiles.Count(x => x.TokenId == token2.TokenId), "ClientProfile is not added");

            // ************
            // *** TEST ***: AddAccessKey by same accessKey shoud just update token
            token1.Name = "Token 1000";
            app.ClientProfileStore.AddAccessKey(token1.ToAccessKey());
            Assert.AreEqual(token1.Name, app.ClientProfileStore.GetToken(token1.TokenId).Name);

            // ************
            // *** TEST ***: SetClientProfile throw KeyNotFoundException exception if tokenId does not exist
            try
            {
                app.ClientProfileStore.SetClientProfile(new ClientProfile
                {
                    Name = "Hi",
                    ClientProfileId = Guid.NewGuid(),
                    TokenId = Guid.NewGuid()
                });
                Assert.Fail("KeyNotFoundException exception was expected!");
            }
            catch (KeyNotFoundException) { }

            // ************
            // *** TEST ***: SetClientProfile should update the old node if ClientProfileId already exists
            app.ClientProfileStore.SetClientProfile(new ClientProfile
            {
                Name = "Hi2",
                ClientProfileId = clientProfile1.ClientProfileId,
                TokenId = clientProfile1.TokenId
            });
            Assert.AreEqual("Hi2", app.ClientProfileStore.ClientProfiles.First(x => x.ClientProfileId == clientProfile1.ClientProfileId).Name);

            // ************
            // *** TEST ***: SetClientProfile should add new ClientProfile if ClientProfileId is new even with used tokenId
            clientProfiles = app.ClientProfileStore.ClientProfiles;
            var clientProfileId3 = Guid.NewGuid();
            app.ClientProfileStore.SetClientProfile(new ClientProfile
            {
                Name = "Test-03",
                ClientProfileId = clientProfileId3,
                TokenId = clientProfile1.TokenId
            });
            Assert.AreEqual(clientProfiles.Length + 1, app.ClientProfileStore.ClientProfiles.Length, "ClientProfile has not beed added!");

            // ************
            // *** TEST ***: RemoveClientProfile should not remove token when other clientProfile still use the token
            app.ClientProfileStore.RemoveClientProfile(clientProfileId3);
            Assert.AreEqual(clientProfiles.Length, app.ClientProfileStore.ClientProfiles.Length, "ClientProfile has not been removed!");
            Assert.IsNotNull(app.ClientProfileStore.GetToken(clientProfile1.TokenId));

            // ************
            // *** TEST ***: RemoveClientProfile should remove token when no clientProfile usinng it
            clientProfiles = app.ClientProfileStore.ClientProfiles;
            app.ClientProfileStore.RemoveClientProfile(clientProfile1.ClientProfileId);
            Assert.AreEqual(clientProfiles.Length - 1, app.ClientProfileStore.ClientProfiles.Length, "ClientProfile has not been removed!");
            try
            {
                app.ClientProfileStore.GetToken(clientProfile1.TokenId);
                Assert.Fail("KeyNotFoundException exception was expected!");
            }
            catch (KeyNotFoundException) { }

            // ************
            // *** TEST ***: ClientProfileItems
            Assert.AreEqual(app.ClientProfileStore.ClientProfiles.Length, app.ClientProfileStore.ClientProfileItems.Length, "ClientProfileItems has invalid length!");
        }

        [TestMethod]
        public void Token_secret_should_not_be_extracted()
        {
            using var app = TestHelper.CreateClientApp();

            // ************
            // *** TEST ***: AddClientProfile should not return then secret
            var token = CreateToken();
            var clientProfile = app.ClientProfileStore.AddAccessKey(token.ToAccessKey());

            // ************
            // *** TEST ***: GetToken should not return then secret
            var token2 = app.ClientProfileStore.GetToken(clientProfile.TokenId);
            Assert.IsTrue(Util.IsNullOrEmpty(token2.Secret), "token should not have secret");

            // ************
            // *** TEST ***: ClientProfileItems should not return then secret
            var clientProfiles = app.ClientProfileStore.ClientProfileItems;
            Assert.IsTrue(clientProfiles.All(x => Util.IsNullOrEmpty(x.Token.Secret)), "token should not have secret");
        }

        [TestMethod]
        public void Save_load_clientProfiles()
        {
            using var app = TestHelper.CreateClientApp();

            var token1 = CreateToken();
            var clientProfile1 = app.ClientProfileStore.AddAccessKey(token1.ToAccessKey());

            var token2 = CreateToken();
            var clientProfile2 = app.ClientProfileStore.AddAccessKey(token2.ToAccessKey());

            var clientProfiles = app.ClientProfileStore.ClientProfiles;
            app.Dispose();

            using var app2 = TestHelper.CreateClientApp(app.AppDataFolderPath);
            Assert.AreEqual(clientProfiles.Length, app2.ClientProfileStore.ClientProfiles.Length, "ClientProfiles count are not same!");
            Assert.IsNotNull(app2.ClientProfileStore.ClientProfiles.First(x => x.ClientProfileId == clientProfile1.ClientProfileId));
            Assert.IsNotNull(app2.ClientProfileStore.ClientProfiles.First(x => x.ClientProfileId == clientProfile2.ClientProfileId));
            Assert.IsNotNull(app2.ClientProfileStore.GetToken(token1.TokenId));
            Assert.IsNotNull(app2.ClientProfileStore.GetToken(token2.TokenId));
        }

        [TestMethod]
        public void State_Diagnose_info()
        {
            // create server
            using var server = TestHelper.CreateServer();
            var token = TestHelper.CreateAccessToken(server);

            // create app
            using var app = TestHelper.CreateClientApp();
            var clientProfile1 = app.ClientProfileStore.AddAccessKey(token.ToAccessKey());

            // ************
            // Test: With diagnose
            var _ = app.Connect(clientProfile1.ClientProfileId, true);
            TestHelper.WaitForClientState(app, AppConnectionState.Connected, timeout: 8000);
            app.ClearLastError(); // should not effect
            app.Disconnect(true);
            TestHelper.WaitForClientState(app, AppConnectionState.None);

            Assert.IsTrue(app.State.LogExists);
            Assert.IsTrue(app.State.HasDiagnoseStarted);
            Assert.IsTrue(app.State.HasDisconnectedByUser);
            Assert.IsTrue(app.State.HasProblemDetected);
            Assert.IsTrue(app.State.IsIdle);

            app.ClearLastError();
            Assert.IsFalse(app.State.HasDiagnoseStarted);
            Assert.IsFalse(app.State.HasDisconnectedByUser);
            Assert.IsFalse(app.State.HasProblemDetected);

            // ************
            // Test: Without diagnose
            _ = app.Connect(clientProfile1.ClientProfileId);
            TestHelper.WaitForClientState(app, AppConnectionState.Connected);
            app.Disconnect(true);
            TestHelper.WaitForClientState(app, AppConnectionState.None);

            Assert.IsFalse(app.State.LogExists);
            Assert.IsFalse(app.State.HasDiagnoseStarted);
            Assert.IsTrue(app.State.HasDisconnectedByUser);
            Assert.IsTrue(app.State.HasProblemDetected); //no data
            Assert.IsTrue(app.State.IsIdle);
        }

        [TestMethod]
        public void State_Error_InConnecting()
        {
            // create server
            using var server = TestHelper.CreateServer();
            var token = TestHelper.CreateAccessToken(server);
            token.ServerEndPoint = IPEndPoint.Parse("10.10.10.99:443");

            // create app
            using var app = TestHelper.CreateClientApp();
            var clientProfile = app.ClientProfileStore.AddAccessKey(token.ToAccessKey());

            try
            {
                app.Connect(clientProfile.ClientProfileId, false).Wait();
            }
            catch { }
            TestHelper.WaitForClientState(app, AppConnectionState.None);
            Assert.IsFalse(app.State.LogExists);
            Assert.IsFalse(app.State.HasDiagnoseStarted);
            Assert.IsTrue(app.State.HasProblemDetected);
            Assert.IsNotNull(app.State.LastError);
        }

        [TestMethod]
        public void Set_DnsServer_to_packetcapture()
        {
            // Create Server
            using var server = TestHelper.CreateServer();
            var token = TestHelper.CreateAccessToken(server);

            // create app
            using var packetCapture = TestHelper.CreatePacketCapture(new() { IsDnsServerSupported = true });
            Assert.IsTrue(packetCapture.DnsServers == null || packetCapture.DnsServers.Length == 0);

            using var client = TestHelper.CreateClient(token, packetCapture);
            TestHelper.WaitForClientState(client, ClientState.Connected);

            Assert.IsTrue(packetCapture.DnsServers != null && packetCapture.DnsServers.Length > 0);
        }

        [TestMethod]
        public void IpFilters()
        {
            IpFiltersInternal(false, false);
            IpFiltersInternal(false, true);
            IpFiltersInternal(true, false);
            IpFiltersInternal(true, true);
        }

        public static void IpFiltersInternal(bool usePassthru, bool isDnsServerSupported)
        {
            var testPing = usePassthru; //ping filter is only supported in passthru mode
            var testDns = !isDnsServerSupported; //dns will work as normal UDP when DnsServerSupported, otherwise it should be redirected

            // Create Server
            using var server = TestHelper.CreateServer();
            var token = TestHelper.CreateAccessToken(server);

            // create app
            TestDeviceOptions deviceOptions = new() { CanSendPacketToOutbound = usePassthru, IsDnsServerSupported = isDnsServerSupported };
            using var app = TestHelper.CreateClientApp(deviceOptions: deviceOptions);
            var clientProfile = app.ClientProfileStore.AddAccessKey(token.ToAccessKey());
            var ipList = Dns.GetHostAddresses(TestHelper.TEST_HttpsUri1.Host)
                    .Select(x => new IpRange(x))
                    .Concat(new[] {
                        new IpRange(TestHelper.TEST_PingAddress1),
                        new IpRange(TestHelper.TEST_NsEndPoint1.Address),
                        new IpRange(TestHelper.TEST_NtpEndPoint1.Address)
                    });

            // ************
            // *** TEST ***: Test Include ip filter
            app.UserSettings.CustomIpRanges = ipList.ToArray();
            app.UserSettings.IpGroupFilters = new[] { "custom" };
            app.UserSettings.IpGroupFiltersMode = FilterMode.Include;
            _ = app.Connect(clientProfile.ClientProfileId);
            TestHelper.WaitForClientState(app, AppConnectionState.Connected);

            IpFilters_Test(app, testInclude: true, testPing: testPing, testUdp: true, testDns: testDns);
            app.Disconnect();

            // ************
            // *** TEST ***: Test Exclude ip filters
            app.UserSettings.IpGroupFiltersMode = FilterMode.Exclude;
            _ = app.Connect(clientProfile.ClientProfileId);
            TestHelper.WaitForClientState(app, AppConnectionState.Connected);

            IpFilters_Test(app, testInclude: false, testPing: testPing, testUdp: true, testDns: testDns);
        }

        public static void IpFilters_Test(VpnHoodApp app, bool testInclude, bool testUdp, bool testPing, bool testDns)
        {
            // ping
            long oldRecievedByteCount;

            // TCP
            oldRecievedByteCount = app.State.RecievedByteCount;
            TestHelper.Test_Https(uri: TestHelper.TEST_HttpsUri1);
            Assert.AreNotEqual(oldRecievedByteCount == app.State.RecievedByteCount, testInclude);

            // TCP
            oldRecievedByteCount = app.State.RecievedByteCount;
            TestHelper.Test_Https(uri: TestHelper.TEST_HttpsUri2);
            Assert.AreEqual(oldRecievedByteCount == app.State.RecievedByteCount, testInclude);


            if (testPing)
            {
                // ping
                oldRecievedByteCount = app.State.RecievedByteCount;
                TestHelper.Test_Ping(ipAddress: TestHelper.TEST_PingAddress1);
                Assert.AreNotEqual(oldRecievedByteCount == app.State.RecievedByteCount, testInclude);

                // ping
                oldRecievedByteCount = app.State.RecievedByteCount;
                TestHelper.Test_Ping(ipAddress: TestHelper.TEST_PingAddress2);
                Assert.AreEqual(oldRecievedByteCount == app.State.RecievedByteCount, testInclude);
            }

            if (testUdp)
            {
                // UDP
                oldRecievedByteCount = app.State.RecievedByteCount;
                TestHelper.Test_Udp(ntpEndPoint: TestHelper.TEST_NtpEndPoint1);
                Assert.AreNotEqual(oldRecievedByteCount == app.State.RecievedByteCount, testInclude);

                // UDP
                oldRecievedByteCount = app.State.RecievedByteCount;
                TestHelper.Test_Udp(ntpEndPoint: TestHelper.TEST_NtpEndPoint2);
                Assert.AreEqual(oldRecievedByteCount == app.State.RecievedByteCount, testInclude);
            }

            // DNS should always use tunnel regarding of any exclude or include option
            if (testDns)
            {
                oldRecievedByteCount = app.State.RecievedByteCount;
                TestHelper.Test_Dns(nsEndPoint: TestHelper.TEST_NsEndPoint1);
                Assert.AreNotEqual(oldRecievedByteCount, app.State.RecievedByteCount);

                oldRecievedByteCount = app.State.RecievedByteCount;
                TestHelper.Test_Dns(nsEndPoint: TestHelper.TEST_NsEndPoint2);
                Assert.AreNotEqual(oldRecievedByteCount, app.State.RecievedByteCount);
            }
        }

        [TestMethod]
        public void State_Connected_Disconnected_successfully()
        {
            // create server
            using var server = TestHelper.CreateServer();
            var token = TestHelper.CreateAccessToken(server);

            // create app
            using var app = TestHelper.CreateClientApp();
            var clientProfile = app.ClientProfileStore.AddAccessKey(token.ToAccessKey());

            var _ = app.Connect(clientProfile.ClientProfileId, false);
            TestHelper.WaitForClientState(app, AppConnectionState.Connected);

            // get data through tunnel
            TestHelper.Test_Https();

            Assert.IsFalse(app.State.LogExists);
            Assert.IsFalse(app.State.HasDiagnoseStarted);
            Assert.IsFalse(app.State.HasProblemDetected);
            Assert.IsNull(app.State.LastError);
            Assert.IsFalse(app.State.IsIdle);

            // test disconnect
            app.Disconnect();
            TestHelper.WaitForClientState(app, AppConnectionState.None);
        }

        [TestMethod]
        public void Get_token_fron_tokenLink()
        {
            // create server
            using var fileAccessServer = TestHelper.CreateFileAccessServer();
            using var testAccessServer = new TestAccessServer(fileAccessServer);
            using var server = TestHelper.CreateServer(accessServer: testAccessServer);

            var token1 = TestHelper.CreateAccessToken(server);
            var token2 = TestHelper.CreateAccessToken(server);

            //create web server and set token url to it
            var endPoint = Util.GetFreeEndPoint(IPAddress.Loopback);
            using var webServer = new WebServer(endPoint.Port);
            token1.Url = $"http://{endPoint}/accesskey";

            // update token1 in webserver
            var isTokenRetreived = false;
            webServer.WithAction("/accesskey", HttpVerbs.Get, context =>
            {
                isTokenRetreived = true;
                return context.SendStringAsync(token2.ToAccessKey(), "text/json", Encoding.UTF8);
            });
            webServer.Start();

            // remove token1 from server
            fileAccessServer.RemoveToken(token1.TokenId).Wait();

            // connect
            using var app = TestHelper.CreateClientApp();
            var clientProfile = app.ClientProfileStore.AddAccessKey(token1.ToAccessKey());
            var _ = app.Connect(clientProfile.ClientProfileId, false);
            TestHelper.WaitForClientState(app, AppConnectionState.Connected);
            Assert.AreEqual(AppConnectionState.Connected, app.State.ConnectionState);
            Assert.IsTrue(isTokenRetreived);

        }

        [TestMethod]
        public void Change_server_while_connected()
        {
            using var server1 = TestHelper.CreateServer();
            using var server2 = TestHelper.CreateServer();

            var token1 = TestHelper.CreateAccessToken(server1);
            var token2 = TestHelper.CreateAccessToken(server2);

            // connect
            using var app = TestHelper.CreateClientApp();
            var clientProfile1 = app.ClientProfileStore.AddAccessKey(token1.ToAccessKey());
            var clientProfile2 = app.ClientProfileStore.AddAccessKey(token2.ToAccessKey());

            app.Connect(clientProfile1.ClientProfileId).GetAwaiter();
            TestHelper.WaitForClientState(app, AppConnectionState.Connected);

            app.Connect(clientProfile2.ClientProfileId).GetAwaiter();
            TestHelper.WaitForClientState(app, AppConnectionState.Connected);

            Assert.AreEqual(AppConnectionState.Connected, app.State.ConnectionState, "Client connection has not been changed!");
        }
    }
}

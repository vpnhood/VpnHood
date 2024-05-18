using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using EmbedIO;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Client;
using VpnHood.Client.App;
using VpnHood.Client.App.ClientProfiles;
using VpnHood.Common;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Logging;
using VpnHood.Common.Net;
using VpnHood.Common.Utils;

// ReSharper disable DisposeOnUsingVariable

namespace VpnHood.Test.Tests;

[TestClass]
public class ClientAppTest : TestBase
{
    private int _lastSupportId;

    private Token CreateToken()
    {
        var randomId = Guid.NewGuid();
        var token = new Token
        {
            Name = "Default Test Server",
            IssuedAt = DateTime.UtcNow,
            SupportId = _lastSupportId++.ToString(),
            TokenId = randomId.ToString(),
            Secret = randomId.ToByteArray(),
            ServerToken = new ServerToken
            {
                HostEndPoints = [IPEndPoint.Parse("127.0.0.1:443")],
                CertificateHash = randomId.ToByteArray(),
                HostName = randomId.ToString(),
                HostPort = 443,
                Secret = randomId.ToByteArray(),
                CreatedTime = DateTime.UtcNow,
                IsValidHostName = false
            }
        };

        return token;
    }

    [TestMethod]
    public async Task BuiltIn_AccessKeys_initialization()
    {
        var appOptions = TestHelper.CreateClientAppOptions();
        var tokens = new[] { CreateToken(), CreateToken() };
        appOptions.AccessKeys = tokens.Select(x => x.ToAccessKey()).ToArray();

        await using var app1 = TestHelper.CreateClientApp(appOptions: appOptions);
        var clientProfiles = app1.ClientProfileService.List();
        Assert.AreEqual(tokens.Length, clientProfiles.Length);
        Assert.AreEqual(tokens[0].TokenId, clientProfiles[0].Token.TokenId);
        Assert.AreEqual(tokens[1].TokenId, clientProfiles[1].Token.TokenId);
        Assert.AreEqual(tokens[0].TokenId, clientProfiles.Single(x => x.ClientProfileId == app1.Features.BuiltInClientProfileId).Token.TokenId);

        // BuiltIn token should not be removed
        foreach (var clientProfile in clientProfiles)
        {
            Assert.ThrowsException<UnauthorizedAccessException>(() =>
            {
                // ReSharper disable once AccessToDisposedClosure
                app1.ClientProfileService.Remove(clientProfile.ClientProfileId);
            });
        }
    }


    [TestMethod]
    public async Task Load_country_ip_groups()
    {
        // ************
        // *** TEST ***: 
        await using var app1 = TestHelper.CreateClientApp();
        var ipGroups = await app1.GetIpGroups();
        Assert.IsFalse(ipGroups.Any(x => x.IpGroupId == "us"),
            "Countries should not be extracted in test due to performance.");
        await app1.DisposeAsync();

        // ************
        // *** TEST ***: 
        var appOptions = TestHelper.CreateClientAppOptions();
        appOptions.UseIpGroupManager = true;
        await using var app2 = TestHelper.CreateClientApp(appOptions: appOptions);
        var ipGroups2 = await app2.GetIpGroups();
        Assert.IsTrue(ipGroups2.Any(x => x.IpGroupId == "us"),
            "Countries has not been extracted.");
    }

    [TestMethod]
    public async Task ClientProfiles_default_ServerLocation()
    {
        await using var app1 = TestHelper.CreateClientApp();

        // test two region in a same country
        var token = CreateToken();
        token.ServerToken.ServerLocations = ["us/regin2", "us/california"];
        var clientProfile = app1.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        app1.UserSettings.ClientProfileId = clientProfile.ClientProfileId;
        Assert.IsNull(app1.State.ServerLocation);
    }


    [TestMethod]
    public async Task ClientProfiles_ServerLocations()
    {
        await using var app1 = TestHelper.CreateClientApp();

        // test two region in a same country
        var token = CreateToken();
        token.ServerToken.ServerLocations = ["us", "us/california"];
        var clientProfile = app1.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        var clientProfileInfo = clientProfile.ToInfo();
        var serverLocations = clientProfileInfo.ServerLocationInfos.Select(x => x.ServerLocation).ToArray();
        var i = 0;
        Assert.AreEqual("us/*", serverLocations[i++]);
        Assert.AreEqual("us/california", serverLocations[i++]);
        Assert.IsFalse(clientProfileInfo.ServerLocationInfos[0].IsNestedCountry);
        Assert.IsTrue(clientProfileInfo.ServerLocationInfos[1].IsNestedCountry);
        _ = i;

        // test multiple countries
        token = CreateToken();
        token.ServerToken.ServerLocations = ["us", "us/california", "uk"];
        clientProfile = app1.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        clientProfileInfo = clientProfile.ToInfo();
        serverLocations = clientProfileInfo.ServerLocationInfos.Select(x => x.ServerLocation).ToArray();
        i = 0;
        Assert.AreEqual("*/*", serverLocations[i++]);
        Assert.AreEqual("uk/*", serverLocations[i++]);
        Assert.AreEqual("us/*", serverLocations[i++]);
        Assert.AreEqual("us/california", serverLocations[i++]);
        _ = i;

        // test multiple countries
        token = CreateToken();
        token.ServerToken.ServerLocations = ["us/virgina", "us/california", "uk/england", "uk/region2", "uk/england"];
        clientProfile = app1.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        clientProfileInfo = clientProfile.ToInfo();
        serverLocations = clientProfileInfo.ServerLocationInfos.Select(x => x.ServerLocation).ToArray();
        i = 0;
        Assert.AreEqual("*/*", serverLocations[i++]);
        Assert.AreEqual("uk/*", serverLocations[i++]);
        Assert.AreEqual("uk/england", serverLocations[i++]);
        Assert.AreEqual("uk/region2", serverLocations[i++]);
        Assert.AreEqual("us/*", serverLocations[i++]);
        Assert.AreEqual("us/california", serverLocations[i++]);
        Assert.AreEqual("us/virgina", serverLocations[i++]);
        Assert.IsFalse(clientProfileInfo.ServerLocationInfos[0].IsNestedCountry);
        Assert.IsFalse(clientProfileInfo.ServerLocationInfos[1].IsNestedCountry);
        Assert.IsTrue(clientProfileInfo.ServerLocationInfos[2].IsNestedCountry);
        Assert.IsTrue(clientProfileInfo.ServerLocationInfos[3].IsNestedCountry);
        _ = i;
    }

    [TestMethod]
    public async Task ClientProfiles_CRUD()
    {
        await using var app = TestHelper.CreateClientApp();

        // ************
        // *** TEST ***: AddAccessKey should add a clientProfile
        var token1 = CreateToken();
        token1.ServerToken.ServerLocations = ["us", "us/california"];
        var clientProfile1 = app.ClientProfileService.ImportAccessKey(token1.ToAccessKey());
        Assert.IsNotNull(app.ClientProfileService.FindByTokenId(token1.TokenId), "ClientProfile is not added");
        Assert.AreEqual(token1.TokenId, clientProfile1.Token.TokenId, "invalid tokenId has been assigned to clientProfile");

        // ************
        // *** TEST ***: AddAccessKey with new accessKey should add another clientProfile
        var token2 = CreateToken();
        app.ClientProfileService.ImportAccessKey(token2.ToAccessKey());
        Assert.IsNotNull(app.ClientProfileService.FindByTokenId(token1.TokenId), "ClientProfile is not added");

        // ************
        // *** TEST ***: AddAccessKey by same accessKey should just update token
        var profileCount = app.ClientProfileService.List().Length;
        token1.Name = "Token 1000";
        app.ClientProfileService.ImportAccessKey(token1.ToAccessKey());
        Assert.AreEqual(token1.Name, app.ClientProfileService.GetToken(token1.TokenId).Name);
        Assert.AreEqual(profileCount, app.ClientProfileService.List().Length);

        // ************
        // *** TEST ***: Update throw NotExistsException exception if tokenId does not exist
        Assert.ThrowsException<NotExistsException>(() =>
        {
            // ReSharper disable once AccessToDisposedClosure
            app.ClientProfileService.Update(Guid.NewGuid(), new ClientProfileUpdateParams
            {
                ClientProfileName = "Hi"
            });

        });

        // ************
        // *** TEST ***: Update should update the old node if ClientProfileId already exists
        app.ClientProfileService.Update(clientProfile1.ClientProfileId, new ClientProfileUpdateParams
        {
            ClientProfileName = "Hi2"
        });
        Assert.AreEqual("Hi2", app.ClientProfileService.Get(clientProfile1.ClientProfileId).ClientProfileName);

        // ************
        // *** TEST ***: RemoveClientProfile
        app.ClientProfileService.Remove(clientProfile1.ClientProfileId);
        Assert.IsNull(app.ClientProfileService.FindById(clientProfile1.ClientProfileId), "ClientProfile has not been removed!");
    }

    [TestMethod]
    public async Task Save_load_clientProfiles()
    {
        await using var app = TestHelper.CreateClientApp();

        var token1 = CreateToken();
        var clientProfile1 = app.ClientProfileService.ImportAccessKey(token1.ToAccessKey());

        var token2 = CreateToken();
        var clientProfile2 = app.ClientProfileService.ImportAccessKey(token2.ToAccessKey());

        var clientProfiles = app.ClientProfileService.List();
        await app.DisposeAsync();

        var appOptions = TestHelper.CreateClientAppOptions();
        appOptions.StorageFolderPath = app.StorageFolderPath;

        await using var app2 = TestHelper.CreateClientApp(appOptions: appOptions);
        Assert.AreEqual(clientProfiles.Length, app2.ClientProfileService.List().Length, "ClientProfiles count are not same!");
        Assert.IsNotNull(app2.ClientProfileService.FindById(clientProfile1.ClientProfileId));
        Assert.IsNotNull(app2.ClientProfileService.FindById(clientProfile2.ClientProfileId));
        Assert.IsNotNull(app2.ClientProfileService.GetToken(token1.TokenId));
        Assert.IsNotNull(app2.ClientProfileService.GetToken(token2.TokenId));
    }

    [TestMethod]
    public async Task State_Diagnose_info()
    {
        // create server
        await using var server = TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // create app
        await using var app = TestHelper.CreateClientApp();
        var clientProfile1 = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());

        // ************
        // Test: With diagnose
        _ = app.Connect(clientProfile1.ClientProfileId, diagnose: true);
        await TestHelper.WaitForClientStateAsync(app, AppConnectionState.Connected, 10000);
        app.ClearLastError(); // should not affect
        await app.Disconnect(true);
        await TestHelper.WaitForClientStateAsync(app, AppConnectionState.None);

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
        await TestHelper.WaitForClientStateAsync(app, AppConnectionState.Connected);
        await app.Disconnect(true);
        await TestHelper.WaitForClientStateAsync(app, AppConnectionState.None);

        Assert.IsFalse(app.State.LogExists);
        Assert.IsFalse(app.State.HasDiagnoseStarted);
        Assert.IsTrue(app.State.HasDisconnectedByUser);
        Assert.IsTrue(app.State.IsIdle);
    }

    [TestMethod]
    public async Task State_Error_InConnecting()
    {
        // create server
        await using var server = TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);
        token.ServerToken.HostEndPoints = [IPEndPoint.Parse("10.10.10.99:443")];

        // create app
        await using var app = TestHelper.CreateClientApp();
        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());

        try
        {
            await app.Connect(clientProfile.ClientProfileId);
        }
        catch
        {
            // ignored
        }

        await TestHelper.WaitForClientStateAsync(app, AppConnectionState.None);
        Assert.IsFalse(app.State.LogExists);
        Assert.IsFalse(app.State.HasDiagnoseStarted);
        Assert.IsTrue(app.State.HasProblemDetected);
        Assert.IsNotNull(app.State.LastError);
    }

    [TestMethod]
    public async Task Set_DnsServer_to_packetCapture()
    {
        // Create Server
        await using var server = TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // create app
        using var packetCapture = TestHelper.CreatePacketCapture(new TestDeviceOptions { IsDnsServerSupported = true });
        Assert.IsTrue(packetCapture.DnsServers == null || packetCapture.DnsServers.Length == 0);

        await using var client = await TestHelper.CreateClient(token, packetCapture);
        await TestHelper.WaitForClientStateAsync(client, ClientState.Connected);

        Assert.IsTrue(packetCapture.DnsServers is { Length: > 0 });
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task IpFilters(bool usePassthru, bool isDnsServerSupported)
    {
        var testDns =
            !isDnsServerSupported; //dns will work as normal UDP when DnsServerSupported, otherwise it should be redirected

        // Create Server
        await using var server = TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // create app
        var deviceOptions = new TestDeviceOptions
        {
            CanSendPacketToOutbound = usePassthru,
            IsDnsServerSupported = isDnsServerSupported,
            CaptureDnsAddresses = TestHelper.TestIpAddresses.ToArray()
        };

        await using var app = TestHelper.CreateClientApp(deviceOptions: deviceOptions);
        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        var customIps = (await Dns.GetHostAddressesAsync(TestConstants.HttpsUri1.Host))
            .Select(x => new IpRange(x))
            .Concat(new[]
            {
                new IpRange(TestConstants.PingV4Address1),
                new IpRange(TestConstants.NsEndPoint1.Address),
                new IpRange(TestConstants.UdpV4EndPoint1.Address),
                new IpRange(TestConstants.UdpV6EndPoint1.Address)
            })
            .ToArray();

        // ************
        // *** TEST ***: Test Include ip filter
        app.UserSettings.IncludeIpRanges = customIps;
        app.UserSettings.ExcludeIpRanges = null;
        await app.Connect(clientProfile.ClientProfileId);
        await TestHelper.WaitForClientStateAsync(app, AppConnectionState.Connected);
        await TestHelper.Test_Ping(ipAddress: TestConstants.PingV4Address1);

        await IpFilters_TestInclude(app, testPing: usePassthru, testUdp: true, testDns: testDns);
        await app.Disconnect();

        // ************
        // *** TEST ***: Test Exclude ip filters
        app.UserSettings.IncludeIpRanges = null;
        app.UserSettings.ExcludeIpRanges = customIps;
        await app.Connect(clientProfile.ClientProfileId);
        await TestHelper.WaitForClientStateAsync(app, AppConnectionState.Connected);

        await IpFilters_TestExclude(app, testPing: usePassthru, testUdp: true, testDns: testDns);
    }

    public static async Task IpFilters_TestInclude(VpnHoodApp app, bool testUdp, bool testPing, bool testDns)
    {
        // TCP
        var oldReceivedByteCount = app.State.SessionTraffic.Received;
        await TestHelper.Test_Https(uri: TestConstants.HttpsUri1);
        Assert.AreNotEqual(oldReceivedByteCount, app.State.SessionTraffic.Received);

        // TCP
        oldReceivedByteCount = app.State.SessionTraffic.Received;
        await TestHelper.Test_Https(uri: TestConstants.HttpsUri2);
        Assert.AreEqual(oldReceivedByteCount, app.State.SessionTraffic.Received);

        if (testPing)
        {
            // ping
            oldReceivedByteCount = app.State.SessionTraffic.Received;
            await TestHelper.Test_Ping(ipAddress: TestConstants.PingV4Address1);
            Assert.AreNotEqual(oldReceivedByteCount, app.State.SessionTraffic.Received);

            // ping
            oldReceivedByteCount = app.State.SessionTraffic.Received;
            try
            {
                await TestHelper.Test_Ping(ipAddress: TestConstants.PingV4Address2, timeout: 1000);
                Assert.Fail("Exception expected as server should not exists.");
            }
            catch (Exception ex)
            {
                Assert.AreEqual(nameof(PingException), ex.GetType().Name);
            }

            Assert.AreEqual(oldReceivedByteCount, app.State.SessionTraffic.Received);
        }

        if (testUdp)
        {
            // UDP
            oldReceivedByteCount = app.State.SessionTraffic.Received;
            await TestHelper.Test_Udp(TestConstants.UdpV4EndPoint1);
            Assert.AreNotEqual(oldReceivedByteCount, app.State.SessionTraffic.Received);

            // UDP
            oldReceivedByteCount = app.State.SessionTraffic.Received;
            try
            {
                await TestHelper.Test_Udp(TestConstants.UdpV4EndPoint2, timeout: 1000);
                Assert.Fail("Exception expected as server should not exists.");
            }
            catch (Exception ex)
            {
                Assert.AreEqual(nameof(OperationCanceledException), ex.GetType().Name);
            }

            Assert.AreEqual(oldReceivedByteCount, app.State.SessionTraffic.Received);
        }

        // DNS should always use tunnel regarding of any exclude or include option
        if (testDns)
        {
            oldReceivedByteCount = app.State.SessionTraffic.Received;
            TestHelper.Test_Dns(nsEndPoint: TestConstants.NsEndPoint1);
            Assert.AreNotEqual(oldReceivedByteCount, app.State.SessionTraffic.Received);

            oldReceivedByteCount = app.State.SessionTraffic.Received;
            TestHelper.Test_Dns(nsEndPoint: TestConstants.NsEndPoint2);
            Assert.AreNotEqual(oldReceivedByteCount, app.State.SessionTraffic.Received);
        }
    }

    public static async Task IpFilters_TestExclude(VpnHoodApp app, bool testUdp, bool testPing, bool testDns)
    {
        // TCP
        var oldReceivedByteCount = app.State.SessionTraffic.Received;
        await TestHelper.Test_Https(uri: TestConstants.HttpsUri1);
        Assert.AreEqual(oldReceivedByteCount, app.State.SessionTraffic.Received);

        // TCP
        oldReceivedByteCount = app.State.SessionTraffic.Received;
        await TestHelper.Test_Https(uri: TestConstants.HttpsUri2);
        Assert.AreNotEqual(oldReceivedByteCount, app.State.SessionTraffic.Received);

        if (testPing)
        {
            // ping
            oldReceivedByteCount = app.State.SessionTraffic.Received;
            try
            {
                await TestHelper.Test_Ping(ipAddress: TestConstants.PingV4Address1, timeout: 1000);
                Assert.Fail("Exception expected as server should not exists.");
            }
            catch (Exception ex)
            {
                Assert.AreEqual(nameof(PingException), ex.GetType().Name);
            }
            Assert.AreEqual(oldReceivedByteCount, app.State.SessionTraffic.Received);

            // ping
            oldReceivedByteCount = app.State.SessionTraffic.Received;
            await TestHelper.Test_Ping(ipAddress: TestConstants.PingV4Address2);
            Assert.AreNotEqual(oldReceivedByteCount, app.State.SessionTraffic.Received);
        }

        if (testUdp)
        {
            // UDP
            VhLogger.Instance.LogTrace("Testing UDP include...");
            oldReceivedByteCount = app.State.SessionTraffic.Received;
            try
            {
                await TestHelper.Test_Udp(udpEndPoint: TestConstants.UdpV4EndPoint1, timeout: 1000);
                Assert.Fail("Exception expected as server should not exists.");
            }
            catch (Exception ex)
            {
                Assert.AreEqual(nameof(OperationCanceledException), ex.GetType().Name);
            }
            Assert.AreEqual(oldReceivedByteCount, app.State.SessionTraffic.Received);

            // UDP
            VhLogger.Instance.LogTrace("Testing UDP exclude...");
            oldReceivedByteCount = app.State.SessionTraffic.Received;
            await TestHelper.Test_Udp(TestConstants.UdpV4EndPoint2);
            Assert.AreNotEqual(oldReceivedByteCount, app.State.SessionTraffic.Received);
        }

        // DNS should always use tunnel regarding of any exclude or include option
        if (testDns)
        {
            oldReceivedByteCount = app.State.SessionTraffic.Received;
            TestHelper.Test_Dns(nsEndPoint: TestConstants.NsEndPoint1);
            Assert.AreNotEqual(oldReceivedByteCount, app.State.SessionTraffic.Received);

            oldReceivedByteCount = app.State.SessionTraffic.Received;
            TestHelper.Test_Dns(nsEndPoint: TestConstants.NsEndPoint2);
            Assert.AreNotEqual(oldReceivedByteCount, app.State.SessionTraffic.Received);
        }
    }

    [TestMethod]
    public async Task State_Connected_Disconnected_successfully()
    {
        // create server
        await using var server = TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // create app
        await using var app = TestHelper.CreateClientApp();
        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());

        _ = app.Connect(clientProfile.ClientProfileId);
        await TestHelper.WaitForClientStateAsync(app, AppConnectionState.Connected);

        // get data through tunnel
        await TestHelper.Test_Https();

        Assert.IsFalse(app.State.LogExists);
        Assert.IsFalse(app.State.HasDiagnoseStarted);
        Assert.IsFalse(app.State.HasProblemDetected);
        Assert.IsNull(app.State.LastError);
        Assert.IsFalse(app.State.IsIdle);

        // test disconnect
        await app.Disconnect();
        await TestHelper.WaitForClientStateAsync(app, AppConnectionState.None);
    }

    [TestMethod]
    public async Task update_server_token_url_from_server()
    {
        // create Access Manager and token
        using var fileAccessManager = TestHelper.CreateFileAccessManager();
        using var testAccessManager = new TestAccessManager(fileAccessManager);
        var token = TestHelper.CreateAccessToken(fileAccessManager);

        // Update ServerTokenUrl after token creation
        const string newTokenUrl = "http://127.0.0.100:6000";
        fileAccessManager.ServerConfig.ServerTokenUrl = newTokenUrl;
        fileAccessManager.ServerConfig.ServerSecret = VhUtil.GenerateKey();
        fileAccessManager.ClearCache();

        // create server and app
        await using var server = TestHelper.CreateServer(testAccessManager);
        await using var app = TestHelper.CreateClientApp();
        var clientProfile1 = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());

        // wait for connect
        await app.Connect(clientProfile1.ClientProfileId);
        await TestHelper.WaitForClientStateAsync(app, AppConnectionState.Connected);

        Assert.AreEqual(fileAccessManager.ServerConfig.ServerTokenUrl, app.ClientProfileService.GetToken(token.TokenId).ServerToken.Url);
        CollectionAssert.AreEqual(fileAccessManager.ServerConfig.ServerSecret, app.ClientProfileService.GetToken(token.TokenId).ServerToken.Secret);
    }

    [TestMethod]
    public async Task update_server_token_from_server_token_url()
    {
        // create update webserver
        var endPoint = VhUtil.GetFreeTcpEndPoint(IPAddress.Loopback);
        using var webServer = new WebServer(endPoint.Port);

        // create server1
        var tcpEndPoint = VhUtil.GetFreeTcpEndPoint(IPAddress.Loopback);
        var fileAccessManagerOptions1 = TestHelper.CreateFileAccessManagerOptions();
        fileAccessManagerOptions1.TcpEndPoints = [tcpEndPoint];
        fileAccessManagerOptions1.ServerTokenUrl = $"http://{endPoint}/accesskey";
        using var fileAccessManager1 = TestHelper.CreateFileAccessManager(fileAccessManagerOptions1);
        using var testAccessManager1 = new TestAccessManager(fileAccessManager1);
        await using var server1 = TestHelper.CreateServer(testAccessManager1);
        var token1 = TestHelper.CreateAccessToken(server1);
        await server1.DisposeAsync();

        // create server 2
        await Task.Delay(1100); // wait for new CreatedTime
        fileAccessManagerOptions1.TcpEndPoints = [VhUtil.GetFreeTcpEndPoint(IPAddress.Loopback, tcpEndPoint.Port + 1)];
        var fileAccessManager2 = TestHelper.CreateFileAccessManager(storagePath: fileAccessManager1.StoragePath, options: fileAccessManagerOptions1);
        using var testAccessManager2 = new TestAccessManager(fileAccessManager2);
        await using var server2 = TestHelper.CreateServer(testAccessManager2);
        var token2 = TestHelper.CreateAccessToken(server2);

        //update web server enc_server_token
        var isTokenRetrieved = false;
        webServer.WithAction("/accesskey", HttpVerbs.Get, context =>
        {
            isTokenRetrieved = true;
            return context.SendStringAsync(token2.ServerToken.Encrypt(), "text/plain", Encoding.UTF8);
        });
        webServer.Start();

        // connect
        await using var app = TestHelper.CreateClientApp();
        var clientProfile = app.ClientProfileService.ImportAccessKey(token1.ToAccessKey());
        await app.Connect(clientProfile.ClientProfileId);

        Assert.IsTrue(isTokenRetrieved);
        Assert.AreNotEqual(token1.ServerToken.CreatedTime, token2.ServerToken.CreatedTime);
        Assert.AreEqual(token2.ServerToken.CreatedTime, app.ClientProfileService.GetToken(token1.TokenId).ServerToken.CreatedTime);
        Assert.AreEqual(AppConnectionState.Connected, app.State.ConnectionState);
    }

    [TestMethod]
    public async Task Change_server_while_connected()
    {
        await using var server1 = TestHelper.CreateServer();
        await using var server2 = TestHelper.CreateServer();

        var token1 = TestHelper.CreateAccessToken(server1);
        var token2 = TestHelper.CreateAccessToken(server2);

        // connect
        await using var app = TestHelper.CreateClientApp();
        var clientProfile1 = app.ClientProfileService.ImportAccessKey(token1.ToAccessKey());
        var clientProfile2 = app.ClientProfileService.ImportAccessKey(token2.ToAccessKey());

        await app.Connect(clientProfile1.ClientProfileId);
        await TestHelper.WaitForClientStateAsync(app, AppConnectionState.Connected);

        await app.Connect(clientProfile2.ClientProfileId);
        await TestHelper.WaitForClientStateAsync(app, AppConnectionState.Connected);

        Assert.AreEqual(AppConnectionState.Connected, app.State.ConnectionState,
            "Client connection has not been changed!");
    }
}
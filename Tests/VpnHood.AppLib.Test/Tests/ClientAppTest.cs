using EmbedIO;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using VpnHood.AppLib.ClientProfiles;
using VpnHood.AppLib.Exceptions;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.IpLocations.Providers.Offlines;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Test;
using VpnHood.Test.Device;

// ReSharper disable DisposeOnUsingVariable

namespace VpnHood.AppLib.Test.Tests;

[TestClass]
public class ClientAppTest : TestAppBase
{
    private async Task UpdateIp2LocationFile()
    {
        // update current ipLocation in app project after a week
        var vhFolder = TestHelper.GetParentDirectory(Directory.GetCurrentDirectory(), 6);
        var solutionFolder = Path.Combine(vhFolder, "VpnHood.AppLib.Assets.IpLocations");
        var projectFolder = Path.Combine(solutionFolder, "VpnHood.AppLib.Assets.Ip2LocationLite");
        var ipLocationFile = Path.Combine(projectFolder, "Resources", "IpLocations.zip");
        VhLogger.Instance.LogInformation("ipLocationFile: {ipLocationFile}", ipLocationFile);
        if (!Directory.Exists(projectFolder))
            throw new DirectoryNotFoundException("Ip2Location Project was not found.");

        // find token
        var userSecretFile = Path.Combine(vhFolder, ".user", "credentials.json");
        var document = JsonDocument.Parse(await File.ReadAllTextAsync(userSecretFile));
        var ip2LocationToken = document.RootElement.GetProperty("Ip2LocationToken").GetString();
        ArgumentException.ThrowIfNullOrWhiteSpace(ip2LocationToken);

        await Ip2LocationDbParser.UpdateLocalDb(ipLocationFile, ip2LocationToken, forIpRange: true);

        // commit project and sync
        try {
            var gitBase = $"--git-dir=\"{solutionFolder}/.git\" --work-tree=\"{solutionFolder}\"";
            await OsUtils.ExecuteCommandAsync("git", $"{gitBase} commit -a -m Publish", CancellationToken.None);
            await OsUtils.ExecuteCommandAsync("git", $"{gitBase} pull", CancellationToken.None);
            await OsUtils.ExecuteCommandAsync("git", $"{gitBase} push", CancellationToken.None);

        }
        catch (ExternalException ex) when (ex.ErrorCode == 1) {
            VhLogger.Instance.LogInformation("Nothing has been updated.");
        }

    }

    [TestMethod]
    public async Task IpLocations_must_be_loaded()
    {
        await UpdateIp2LocationFile();

        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.UseInternalLocationService = true;
        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions);
        var countryCodes = await app.IpRangeLocationProvider.GetCountryCodes();
        Assert.IsTrue(countryCodes.Any(x => x == "US"),
            "Countries has not been extracted.");

        // make sure GetIpRange works
        Assert.IsTrue((await app.IpRangeLocationProvider.GetIpRanges("US")).Any());
    }


    [TestMethod]
    public async Task State_Diagnose_info()
    {
        // create server
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // create app
        await using var app = TestAppHelper.CreateClientApp();
        var clientProfile1 = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());

        // ************
        // Test: With diagnose
        await app.Connect(clientProfile1.ClientProfileId, diagnose: true);
        await app.WaitForState(AppConnectionState.Connected, 10000);
        await app.Disconnect();
        await app.WaitForState(AppConnectionState.None);
        Assert.IsTrue(app.State.LogExists);
        Assert.IsTrue(app.State.HasDiagnoseRequested);
        Assert.IsTrue(app.State.HasDisconnectedByUser);
        Assert.IsTrue(app.State.IsIdle);
        Assert.AreEqual(nameof(NoErrorFoundException), app.State.LastError?.TypeName);

        app.ClearLastError();
        Assert.IsFalse(app.State.HasDiagnoseRequested);
        Assert.IsFalse(app.State.HasDisconnectedByUser);
        Assert.IsNull(app.State.LastError);

        // ************
        // Test: Without diagnose
        await app.Connect(clientProfile1.ClientProfileId);
        await app.WaitForState(AppConnectionState.Connected);
        await app.Disconnect();
        await app.WaitForState(AppConnectionState.None);

        Assert.IsTrue(app.State.IsIdle);
        Assert.IsFalse(app.State.HasDiagnoseRequested);
        Assert.IsTrue(app.State.HasDisconnectedByUser);
        Assert.IsTrue(app.State.LogExists);
    }

    [TestMethod]
    public async Task State_Error_Unreachable_Server()
    {
        // create server
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);
        token.ServerToken.HostEndPoints = [IPEndPoint.Parse("10.10.10.99:443")];

        // create app
        await using var app = TestAppHelper.CreateClientApp();
        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        await Assert.ThrowsExceptionAsync<UnreachableServerException>(() => app.Connect(clientProfile.ClientProfileId));

        await app.WaitForState(AppConnectionState.None);
        Assert.IsTrue(app.State.LogExists);
        Assert.IsFalse(app.State.HasDiagnoseRequested);
        Assert.IsNotNull(app.State.LastError);
    }


    [TestMethod]
    public async Task State_Waiting()
    {
        // create Access Manager and token
        using var accessManager = TestHelper.CreateAccessManager();
        var token = TestHelper.CreateAccessToken(accessManager);

        // create server
        await using var server1 = await TestHelper.CreateServer(accessManager);

        // create app & connect
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.SessionTimeout = TimeSpan.FromSeconds(20);
        appOptions.ReconnectTimeout = TimeSpan.FromSeconds(1);
        appOptions.AutoWaitTimeout = TimeSpan.FromSeconds(2);
        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions, device: TestHelper.CreateDevice());
        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(clientProfile.ClientProfileId);
        await app.WaitForState(AppConnectionState.Connected);

        // dispose server and wait for waiting state
        await server1.DisposeAsync();
        await VhTestUtil.AssertEqualsWait(AppConnectionState.Waiting, async () => {
            await TestHelper.Test_Https(throwError: false, timeout: TimeSpan.FromMilliseconds(100));
            return app.State.ConnectionState;
        });

        // start a new server & waiting for connected state
        await using var server2 = await TestHelper.CreateServer(accessManager);
        await VhTestUtil.AssertEqualsWait(AppConnectionState.Connected, async () => {
            await TestHelper.Test_Https(throwError: false, timeout: TimeSpan.FromMilliseconds(100));
            return app.State.ConnectionState;
        });
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task IpFilters(bool include)
    {
        var device = TestHelper.CreateDevice(new TestVpnAdapterOptions {
            SimulateDns = false
        });

        // Create Server
        await using var server = await TestHelper.CreateServer(socketFactory: device.SocketFactory);
        var token = TestHelper.CreateAccessToken(server);

        // create app
        await using var app = TestAppHelper.CreateClientApp(device: device);
        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());

            // add url2 and endpoint 2
        var httpsExternalUriIps = await Dns.GetHostAddressesAsync(TestConstants.HttpsExternalUri1.Host);
        var customIps = httpsExternalUriIps.Select(x => new IpRange(x)).ToList();
        customIps.Add(new IpRange(TestConstants.NsEndPoint1.Address));
        customIps.Add(new IpRange(TestConstants.PingV4Address1));

        // ************
        // *** TEST ***: Test Include ip filter
        if (include) {
            app.SettingsService.IpFilterSettings.AppIpFilterIncludes = customIps.ToText();
            app.SettingsService.IpFilterSettings.AppIpFilterExcludes = "";
            await app.Connect(clientProfile.ClientProfileId);
            await app.WaitForState(AppConnectionState.Connected);
            await TestHelper.Test_Ping(ipAddress: TestConstants.PingV4Address1);

            VhLogger.Instance.LogDebug(GeneralEventId.Test, "Starting IpFilters_TestInclude...");
            await IpFilters_AssertInclude(app, TestConstants.NsEndPoint1, TestConstants.HttpsExternalUri1);
            await IpFilters_AssertExclude(app, TestConstants.NsEndPoint2, TestConstants.HttpsExternalUri2);
            await app.Disconnect();
        }

        // ************
        // *** TEST ***: Test Exclude ip filters
        if (!include) {
            app.SettingsService.IpFilterSettings.AppIpFilterIncludes = "";
            app.SettingsService.IpFilterSettings.AppIpFilterExcludes = customIps.ToText();
            await app.Connect(clientProfile.ClientProfileId);
            await app.WaitForState(AppConnectionState.Connected);

            VhLogger.Instance.LogDebug(GeneralEventId.Test, "Starting IpFilters_TestExclude...");
            await IpFilters_AssertInclude(app, TestConstants.NsEndPoint2, TestConstants.HttpsExternalUri2);
            await IpFilters_AssertExclude(app, TestConstants.NsEndPoint1, TestConstants.HttpsExternalUri1);
            await app.Disconnect();
        }
    }

    private async Task IpFilters_AssertInclude(VpnHoodApp app, IPEndPoint nameserver, Uri url, int delta = 200)
    {
        // NameServer
        var oldSessionTraffic = app.GetSessionStatus().SessionTraffic;
        var oldSplitTraffic = app.GetSessionStatus().SessionSplitTraffic;
        await TestHelper.Test_UdpByDNS(nameserver);
        Assert.AreNotEqual(oldSessionTraffic, app.GetSessionStatus().SessionTraffic);
        Assert.AreEqual(oldSplitTraffic, app.GetSessionStatus().SessionSplitTraffic);

        // Http
        oldSessionTraffic = app.GetSessionStatus().SessionTraffic;
        oldSplitTraffic = app.GetSessionStatus().SessionSplitTraffic;
        await TestHelper.Test_Https(url);
        Assert.AreNotEqual(oldSessionTraffic.Received, app.GetSessionStatus().SessionTraffic.Received, delta: delta);
        Assert.AreNotEqual(oldSessionTraffic.Sent, app.GetSessionStatus().SessionTraffic.Sent, delta: delta);
        Assert.AreEqual(oldSplitTraffic.Received, app.GetSessionStatus().SessionSplitTraffic.Received, delta: delta);
        Assert.AreEqual(oldSplitTraffic.Sent, app.GetSessionStatus().SessionSplitTraffic.Sent, delta: delta);
    }

    private async Task IpFilters_AssertExclude(VpnHoodApp app, IPEndPoint nameserver, Uri url, int delta = 200)
    {
        // NameServer
        var oldSessionTraffic = app.GetSessionStatus().SessionTraffic;
        var oldSplitTraffic = app.GetSessionStatus().SessionSplitTraffic;
        await TestHelper.Test_UdpByDNS(nameserver);
        Assert.AreEqual(oldSessionTraffic, app.GetSessionStatus().SessionTraffic, 
            $"Udp to {nameserver} should go to tunnel.");

        Assert.AreNotEqual(oldSplitTraffic, app.GetSessionStatus().SessionSplitTraffic, 
            $"Udp to {nameserver} should go not be split.");

        // Http
        oldSessionTraffic = app.GetSessionStatus().SessionTraffic;
        oldSplitTraffic = app.GetSessionStatus().SessionSplitTraffic;
        await TestHelper.Test_Https(url);
        Assert.AreEqual(oldSessionTraffic.Received, app.GetSessionStatus().SessionTraffic.Received, delta: delta);
        Assert.AreEqual(oldSessionTraffic.Sent, app.GetSessionStatus().SessionTraffic.Sent, delta: delta);
        Assert.AreNotEqual(oldSplitTraffic.Received, app.GetSessionStatus().SessionSplitTraffic.Received, delta: delta);
        Assert.AreNotEqual(oldSplitTraffic.Sent, app.GetSessionStatus().SessionSplitTraffic.Sent, delta: delta);
    }


    [TestMethod]
    public async Task Connect_fail_ConnectionTimeoutException()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // create device
        using var testDevice = TestHelper.CreateNullDevice();
        testDevice.StartServiceDelay = TimeSpan.FromSeconds(100);

        // create app
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.ConnectTimeout = TimeSpan.FromSeconds(1);
        await using var app = TestAppHelper.CreateClientApp(appOptions, testDevice);
        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());

        await Assert.ThrowsExceptionAsync<ConnectionTimeoutException>(() => app.Connect(clientProfile.ClientProfileId));
        await app.WaitForState(AppConnectionState.None);
        Assert.AreEqual(nameof(ConnectionTimeoutException), app.State.LastError?.TypeName);
    }


    [TestMethod]
    public async Task Connected_Disconnected_success()
    {
        // create server
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // create app
        await using var app = TestAppHelper.CreateClientApp(device: TestAppHelper.CreateDevice());
        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());

        await app.Connect(clientProfile.ClientProfileId);
        await app.WaitForState(AppConnectionState.Connected);

        // get data through tunnel
        await TestHelper.Test_Https();

        Assert.IsTrue(app.State.LogExists);
        Assert.IsFalse(app.State.HasDiagnoseRequested);
        Assert.IsNull(app.State.LastError);
        Assert.IsFalse(app.State.IsIdle);

        // test disconnect
        await app.Disconnect();
        await app.WaitForState(AppConnectionState.None);
    }

    [TestMethod]
    public async Task update_server_token_url_from_server()
    {
        // create Access Manager and token
        using var accessManager = TestHelper.CreateAccessManager();
        var token = TestHelper.CreateAccessToken(accessManager);

        // Update ServerTokenUrl after token creation
        const string newTokenUrl = "http://127.0.0.100:6000";
        accessManager.ServerConfig.ServerTokenUrls = [newTokenUrl];
        accessManager.ServerConfig.ServerSecret = VhUtils.GenerateKey(); // It can not be changed in new version
        accessManager.ClearCache();

        // create server and app
        await using var server = await TestHelper.CreateServer(accessManager);
        await using var app = TestAppHelper.CreateClientApp();
        var clientProfile1 = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());

        // wait for connect
        await app.Connect(clientProfile1.ClientProfileId);
        await app.WaitForState(AppConnectionState.Connected);

        Assert.AreEqual(accessManager.ServerConfig.ServerTokenUrls.First(),
            app.ClientProfileService.GetToken(token.TokenId).ServerToken.Urls?.First());

        CollectionAssert.AreEqual(accessManager.ServerConfig.ServerSecret,
            app.ClientProfileService.GetToken(token.TokenId).ServerToken.Secret);

        // code should not exist any return objects
        Assert.IsFalse(app.State.LastError?.Data.ContainsKey("AccessCode") == true);
    }

    [TestMethod]
    public async Task update_token_from_server()
    {
        // create Access Manager and token
        using var accessManager = TestHelper.CreateAccessManager();
        var token = TestHelper.CreateAccessToken(accessManager, expirationTime: DateTime.UtcNow.AddDays(-1));
        var orgTokenName = token.Name;

        // Update ServerTokenUrl after token creation
        token.Name = Guid.NewGuid().ToString();

        // create server and app
        await using var server = await TestHelper.CreateServer(accessManager);
        await using var app = TestAppHelper.CreateClientApp();
        var clientProfile1 = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());

        // wait for connect error
        var ex = await Assert.ThrowsExceptionAsync<SessionException>(() => app.Connect(clientProfile1.ClientProfileId));
        Assert.AreEqual(SessionErrorCode.AccessExpired, ex.SessionResponse.ErrorCode);

        // token name must be updated
        var token2 = app.ClientProfileService.GetToken(token.TokenId);
        Assert.AreEqual(orgTokenName, token2.Name);
    }

    [TestMethod]
    public void update_server_token_from_server_token_url_foo() //todo
    {
        update_server_token_from_server_token_url().GetAwaiter().GetResult();
    }


    [TestMethod]
    public async Task update_server_token_from_server_token_url()
    {
        // create update webserver
        var endPoint1 = VhUtils.GetFreeTcpEndPoint(IPAddress.Loopback);
        var endPoint2 = VhUtils.GetFreeTcpEndPoint(IPAddress.Loopback);
        using var webServer1 = new WebServer(endPoint1.Port);
        using var webServer2 = new WebServer(endPoint2.Port);

        // create server1
        var tcpEndPoint = VhUtils.GetFreeTcpEndPoint(IPAddress.Loopback);
        var fileAccessManagerOptions1 = TestHelper.CreateFileAccessManagerOptions();
        fileAccessManagerOptions1.TcpEndPoints = [tcpEndPoint];
        fileAccessManagerOptions1.ServerTokenUrls = [$"http://{endPoint1}/accesskey", $"http://{endPoint2}/accesskey"];
        using var accessManager1 = TestHelper.CreateAccessManager(fileAccessManagerOptions1);
        await using var server1 = await TestHelper.CreateServer(accessManager1);
        var token1 = TestHelper.CreateAccessToken(server1);
        await server1.DisposeAsync();

        // create server 2
        await Task.Delay(1100); // wait for new CreatedTime
        fileAccessManagerOptions1.TcpEndPoints = [VhUtils.GetFreeTcpEndPoint(IPAddress.Loopback, tcpEndPoint.Port + 1)];
        var accessManager2 = TestHelper.CreateAccessManager(storagePath: accessManager1.StoragePath,
            options: fileAccessManagerOptions1);
        await using var server2 = await TestHelper.CreateServer(accessManager2);
        var token2 = TestHelper.CreateAccessToken(server2);

        //update web server enc_server_token
        var isTokenRetrieved = false;
        webServer1.WithAction("/accesskey", HttpVerbs.Get, context => {
            isTokenRetrieved = true;
            return context.SendStringAsync("something_wrong", "text/plain", Encoding.UTF8);
        });

        webServer2.WithAction("/accesskey", HttpVerbs.Get, context => {
            isTokenRetrieved = true;
            return context.SendStringAsync(token2.ServerToken.Encrypt(), "text/plain", Encoding.UTF8);
        });

        webServer1.Start();
        webServer2.Start();


        // connect
        await using var app = TestAppHelper.CreateClientApp();
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
        await using var server1 = await TestHelper.CreateServer();
        await using var server2 = await TestHelper.CreateServer();

        var token1 = TestHelper.CreateAccessToken(server1);
        var token2 = TestHelper.CreateAccessToken(server2);

        // connect
        await using var app = TestAppHelper.CreateClientApp();
        var clientProfile1 = app.ClientProfileService.ImportAccessKey(token1.ToAccessKey());
        var clientProfile2 = app.ClientProfileService.ImportAccessKey(token2.ToAccessKey());

        await app.Connect(clientProfile1.ClientProfileId);
        await app.WaitForState(AppConnectionState.Connected);

        await app.Connect(clientProfile2.ClientProfileId);
        await app.WaitForState(AppConnectionState.Connected);

        Assert.AreEqual(AppConnectionState.Connected, app.State.ConnectionState,
            "Could not connect to new server!");
    }

    [TestMethod]
    public async Task IncludeDomains()
    {
        // first create device to access its socket factory
        var vpnAdapterOptions = TestHelper.CreateTestVpnAdapterOptions();
        var device = TestHelper.CreateDevice(vpnAdapterOptions);

        // Create Server
        await using var server = await TestHelper.CreateServer(socketFactory: device.SocketFactory);

        // create app
        await using var app = TestAppHelper.CreateClientApp(device: device);
        app.UserSettings.DomainFilter.Excludes = [TestConstants.HttpsExternalUri1.Host];

        // connect
        var token = TestHelper.CreateAccessToken(server);
        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(clientProfile.ClientProfileId, diagnose: true);
        await app.WaitForState(AppConnectionState.Connected);

        // text include
        var oldTcpTunnelledCount = app.GetSessionStatus().TcpTunnelledCount;
        var oldTcpPassthruCount = app.GetSessionStatus().TcpPassthruCount;
        await TestHelper.Test_Https(uri: TestConstants.HttpsExternalUri1);
        Assert.AreEqual(oldTcpTunnelledCount, app.GetSessionStatus().TcpTunnelledCount);
        Assert.AreEqual(oldTcpPassthruCount + 1, app.GetSessionStatus().TcpPassthruCount);

        // text exclude
        oldTcpTunnelledCount = app.GetSessionStatus().TcpTunnelledCount;
        oldTcpPassthruCount = app.GetSessionStatus().TcpPassthruCount;
        await TestHelper.Test_Https(uri: TestConstants.HttpsExternalUri2);
        Assert.AreEqual(oldTcpTunnelledCount + 1, app.GetSessionStatus().TcpTunnelledCount);
        Assert.AreEqual(oldTcpPassthruCount, app.GetSessionStatus().TcpPassthruCount);
    }

    [TestMethod]
    public async Task ExcludeDomains()
    {
        // first create device to access its socket factory
        var vpnAdapterOptions = TestHelper.CreateTestVpnAdapterOptions();
        var device = TestHelper.CreateDevice(vpnAdapterOptions);

        // Create Server
        await using var server = await TestHelper.CreateServer(socketFactory: device.SocketFactory);

        // create app
        await using var app = TestAppHelper.CreateClientApp(device: device);
        app.UserSettings.DomainFilter.Excludes = [TestConstants.HttpsExternalUri1.Host];

        // connect
        var token = TestHelper.CreateAccessToken(server);
        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(clientProfile.ClientProfileId, diagnose: true);
        await app.WaitForState(AppConnectionState.Connected);

        // text include
        var oldTcpTunnelledCount = app.GetSessionStatus().TcpTunnelledCount;
        var oldTcpPassthruCount = app.GetSessionStatus().TcpPassthruCount;
        await TestHelper.Test_Https(uri: TestConstants.HttpsUri2);
        Assert.AreEqual(oldTcpTunnelledCount + 1, app.GetSessionStatus().TcpTunnelledCount);
        Assert.AreEqual(oldTcpPassthruCount, app.GetSessionStatus().TcpPassthruCount);

        // text exclude
        oldTcpTunnelledCount = app.GetSessionStatus().TcpTunnelledCount;
        oldTcpPassthruCount = app.GetSessionStatus().TcpPassthruCount;
        await TestHelper.Test_Https(uri: TestConstants.HttpsExternalUri1);
        Assert.AreEqual(oldTcpTunnelledCount, app.GetSessionStatus().TcpTunnelledCount);
        Assert.AreEqual(oldTcpPassthruCount + 1, app.GetSessionStatus().TcpPassthruCount);
    }

    [TestMethod]
    public async Task Premium_token_must_create_premium_session()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        await using var app = TestAppHelper.CreateClientApp();
        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(clientProfile.ClientProfileId, diagnose: true);


        Assert.IsTrue(app.State.ClientProfile?.IsPremiumAccount);
        Assert.IsTrue(app.State.SessionInfo?.IsPremiumSession);
    }

    [TestMethod]
    public async Task ServerLocation_must_reset_to_default_for_no_server_error()
    {
        // Create Server 1
        using var accessManager = TestHelper.CreateAccessManager(serverLocation: "US/california");
        await using var server = await TestHelper.CreateServer(accessManager);

        // Create Client
        var token = accessManager.CreateToken();
        token.ServerToken.ServerLocations = ["US", "FR/Paris"];

        // Create App
        await using var clientApp = TestAppHelper.CreateClientApp();
        var clientProfile = clientApp.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        clientApp.ClientProfileService.Update(clientProfile.ClientProfileId, new ClientProfileUpdateParams {
            SelectedLocation = "FR/Paris"
        });

        // Connect
        try {
            await clientApp.Connect(clientProfile.ClientProfileId);
            Assert.Fail("SessionException was expected.");
        }
        catch (SessionException ex) {
            Assert.AreEqual(SessionErrorCode.NoServerAvailable, ex.SessionResponse.ErrorCode);
        }

        // reload clientProfile
        clientProfile = clientApp.ClientProfileService.Get(clientProfile.ClientProfileId);
        Assert.IsTrue(clientProfile.ToInfo().SelectedLocationInfo?.IsAuto);
    }
}
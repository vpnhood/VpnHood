using VpnHood.AppLib.Abstractions.Ads;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.App;
using VpnHood.AppLib.App.Services.Ads;
using VpnHood.AppLib.Test.Providers;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;
using VpnHood.Core.Client.VpnServices.Abstractions.Tracking;
using VpnHood.Test.Device;
using ChannelProtocol = VpnHood.AppLib.Api.Sessions.ChannelProtocol;

namespace VpnHood.AppLib.Test.Tests;

// The app offers and asks only what its VPN engine reports it has (AppOptions.VpnEngineInfo), and writes the
// options in a contract version both speak.
[TestClass]
public class VpnEngineTest : TestAppBase
{
    private static VpnEngineInfo CreateEngine(VpnEngineFeatures features, int minVersion = 1, int maxVersion = 1) =>
        new() {
            Name = "Test engine",
            Version = "1.0.0",
            MinContractVersion = minVersion,
            MaxContractVersion = maxVersion,
            Features = features
        };

    [TestMethod]
    public async Task The_app_offers_nothing_its_engine_lacks()
    {
        using var accessManager = await TestHelper.CreateAccessManager();
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.VpnEngineInfo = CreateEngine(new VpnEngineFeatures());
        appOptions.AdProviderItems = [
            new AppAdProviderItem { AdProvider = new TestAdProvider(accessManager), ProviderName = "UnitTestAd" }
        ];
        var device = new TestDevice(TestAppHelper, _ => new TestNullVpnAdapter()) {
            KillSwitch = new TestVpnKillSwitch(),
            IsAlwaysOnSupported = true
        };
        await using var app = TestAppHelper.CreateClientApp(appOptions, device);

        Assert.IsFalse(app.Descriptor.IsProxySupported);
        Assert.IsFalse(app.Descriptor.IsKillSwitchSupported);
        Assert.IsFalse(app.Descriptor.IsAdSupported);
        Assert.IsFalse(app.Descriptor.IsRewardedAdSupported);
        Assert.AreEqual(AlwaysOnSupport.None, app.Descriptor.AlwaysOnSupport);
        Assert.IsFalse(app.Descriptor.IsQuicSupported);
        Assert.IsFalse(app.Descriptor.AllowEndPointStrategy);
        CollectionAssert.DoesNotContain(app.Descriptor.ChannelProtocols.ToArray(), ChannelProtocol.Quic);
    }

    [TestMethod]
    public async Task The_app_offers_what_the_device_and_its_engine_both_have()
    {
        var device = new TestDevice(TestAppHelper, _ => new TestNullVpnAdapter()) {
            KillSwitch = new TestVpnKillSwitch(),
            IsAlwaysOnSupported = true
        };
        await using var app = TestAppHelper.CreateClientApp(device: device);

        Assert.IsTrue(app.Descriptor.IsProxySupported);
        Assert.IsTrue(app.Descriptor.IsKillSwitchSupported);
        Assert.AreEqual(AlwaysOnSupport.App, app.Descriptor.AlwaysOnSupport);
    }

    [TestMethod]
    public async Task The_options_carry_the_version_and_ask_nothing_the_engine_lacks()
    {
        await using var server = await TestHelper.CreateServer();
        var device = new TestDevice(TestAppHelper, _ => new TestNullVpnAdapter());
        var appOptions = TestAppHelper.CreateAppOptions(isDebugMode: false);
        appOptions.TrackerFactories = [new Ga4TrackerFactory()];
        appOptions.VpnEngineInfo = CreateEngine(new VpnEngineFeatures { TrackerKinds = [TrackerKinds.DotNet] }, maxVersion: 9);
        await using var app = TestAppHelper.CreateClientApp(appOptions, device);

        var vpnProfile = app.VpnProfileService.ImportAccessKey((await TestHelper.CreateAccessToken(server)).ToAccessKey());
        await app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);
        await app.WaitForState(AppConnectionState.Connected, TestCt);

        var options = await new VpnServiceOptionsFile(device.VpnServiceConfigFolder).Read(TestCt);
        Assert.AreEqual(VpnServiceContract.Version, options.ContractVersion);
        Assert.IsNull(options.ProxyOptions);
        Assert.IsEmpty(options.ClientOptions.TrackerFactoryInfos, "the engine has no GA4 tracker");
        Assert.IsFalse(options.ClientOptions.AllowAlwaysOn);
    }

    [TestMethod]
    public async Task An_engine_that_shares_no_version_fails_the_connect()
    {
        await using var server = await TestHelper.CreateServer();
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.VpnEngineInfo = CreateEngine(VpnEngineFeatures.All,
            minVersion: VpnServiceContract.Version + 1, maxVersion: VpnServiceContract.Version + 1);
        await using var app = TestAppHelper.CreateClientApp(appOptions,
            new TestDevice(TestAppHelper, _ => new TestNullVpnAdapter()));

        var vpnProfile = app.VpnProfileService.ImportAccessKey((await TestHelper.CreateAccessToken(server)).ToAccessKey());
        await Assert.ThrowsExactlyAsync<VpnServiceContractException>(() =>
            app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt));
    }
}

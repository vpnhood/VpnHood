using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.ClientProfiles;
using VpnHood.AppLib.Services.Ads;
using VpnHood.AppLib.Test.Providers;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Server;
using VpnHood.Test.AccessManagers;
using VpnHood.Test.Device;

namespace VpnHood.AppLib.Test.Dom;

public class AppClientServerDom : IDisposable
{
    public TestAppHelper TestAppHelper { get; }
    public TestAccessManager AccessManager { get; }
    public TestAdProvider TestAdProvider { get; }
    public VpnHoodServer Server { get; }
    public VpnHoodApp App { get; }
    public ClientProfile ClientProfile { get; }

    private AppClientServerDom(
        TestAppHelper testAppHelper,
        TestAccessManager accessManager,
        VpnHoodServer server,
        AppOptions appOptions,
        AppAdType adProviderAdType,
        IDevice? device)
    {
        TestAppHelper = testAppHelper;
        AccessManager = accessManager;
        Server = server;
        TestAdProvider = new TestAdProvider(accessManager, adProviderAdType);
        if (!appOptions.AdProviderItems.Any()) {
            appOptions.AdProviderItems = [
                new AppAdProviderItem {
                    AdProvider = TestAdProvider,
                    ProviderName = "UnitTestAd"
                }
            ];
        }

        // create a toke
        var token = testAppHelper.CreateAccessToken(server);

        // create app
        App = testAppHelper.CreateClientApp(device: device, appOptions: appOptions);
        ClientProfile = App.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        App.UserSettings.ClientProfileId = ClientProfile.ClientProfileId;
        App.SettingsService.Save();
    }


    public static async Task<AppClientServerDom> CreateWithNullCapture(
        TestAppHelper testAppHelper,
        AppOptions? appOptions = null)
    {
        var accessManager = testAppHelper.CreateAccessManager();
        appOptions ??= testAppHelper.CreateAppOptions();

        // create server
        var server = await testAppHelper.CreateServer(accessManager: accessManager);
        return new AppClientServerDom(testAppHelper,
            accessManager: accessManager,
            server: server,
            device: null,
            adProviderAdType: AppAdType.RewardedAd,
            appOptions: appOptions);
    }

    public static async Task<AppClientServerDom> Create(
        TestAppHelper testAppHelper,
        TestVpnAdapterOptions? adapterOptions = null,
        AppAdType adProviderAdType = AppAdType.RewardedAd,
        AppOptions? appOptions = null)
    {
        var device = testAppHelper.CreateDevice(adapterOptions);
        var accessManager = testAppHelper.CreateAccessManager();
        appOptions ??= testAppHelper.CreateAppOptions();

        // create server
        var server = await testAppHelper.CreateServer(
            accessManager: accessManager,
            socketFactory: device.SocketFactory);

        return new AppClientServerDom(testAppHelper,
            accessManager: accessManager,
            server: server,
            appOptions: appOptions,
            adProviderAdType: adProviderAdType,
            device: device);
    }

    public Task Connect(ConnectPlanId planId = ConnectPlanId.Normal, CancellationToken cancellationToken = default)
    {
        return App.Connect(
            clientProfileId: ClientProfile.ClientProfileId, 
            planId: planId, 
            cancellationToken: cancellationToken);
    }

    public void Dispose()
    {
        App.Dispose();
        Server.Dispose();
        AccessManager.Dispose();
    }
}
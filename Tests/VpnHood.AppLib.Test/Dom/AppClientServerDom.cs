using VpnHood.AppLib.ClientProfiles;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Server;
using VpnHood.Test.AccessManagers;
using VpnHood.Test.Device;

namespace VpnHood.AppLib.Test.Dom;

public class AppClientServerDom : IDisposable
{
    public TestAppHelper TestAppHelper { get; }
    public TestAccessManager AccessManager { get; }
    public VpnHoodServer Server { get; }
    public VpnHoodApp App { get; }
    public ClientProfile ClientProfile { get; }

    private AppClientServerDom(
        TestAppHelper testAppHelper, 
        TestAccessManager accessManager, 
        VpnHoodServer server,
        IDevice? device)
    {
        TestAppHelper = testAppHelper;
        AccessManager = accessManager;
        Server = server;

        // create a toke
        var token = testAppHelper.CreateAccessToken(server);

        // create app
        var options = testAppHelper.CreateAppOptions();
        App = testAppHelper.CreateClientApp(device: device, appOptions: options);
        ClientProfile = App.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        App.UserSettings.ClientProfileId = ClientProfile.ClientProfileId;
        App.SettingsService.Save();
    }


    public static async Task<AppClientServerDom> CreateWithNullCapture(TestAppHelper testAppHelper)
    {
        var accessManager = testAppHelper.CreateAccessManager();

        // create server
        var server = await testAppHelper.CreateServer(accessManager: accessManager);
        return new AppClientServerDom(testAppHelper, 
            accessManager: accessManager,
            server: server, device: null);
    }

    public static async Task<AppClientServerDom> Create(
        TestAppHelper testAppHelper,
        TestVpnAdapterOptions? adapterOptions = null)
    {
        var device = testAppHelper.CreateDevice(adapterOptions);
        var accessManager = testAppHelper.CreateAccessManager();

        // create server
        var server = await testAppHelper.CreateServer(
            accessManager: accessManager,
            socketFactory: device.SocketFactory);

        return new AppClientServerDom(testAppHelper,
            accessManager: accessManager,
            server: server,
            device: device);
    }

    public Task Connect(CancellationToken cancellationToken)
    {
        return App.Connect(cancellationToken: cancellationToken);
    }

    public void Dispose()
    {
        App.Dispose();
        Server.Dispose();
        AccessManager.Dispose();
    }
}
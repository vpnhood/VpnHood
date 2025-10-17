using VpnHood.AppLib.ClientProfiles;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Server;
using VpnHood.Test.Device;

namespace VpnHood.AppLib.Test;

public class AppClientServerDom : IDisposable
{
    public TestAppHelper TestAppHelper { get; }
    public VpnHoodServer Server { get; }
    public VpnHoodApp App { get; }
    public ClientProfile ClientProfile { get; }

    private AppClientServerDom(TestAppHelper testAppHelper, VpnHoodServer server, IDevice? device)
    {
        TestAppHelper = testAppHelper;
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
        // create server
        var server = await testAppHelper.CreateServer();
        return new AppClientServerDom(testAppHelper, server, null);
    }

    public static async Task<AppClientServerDom> Create(TestAppHelper testAppHelper, 
        TestVpnAdapterOptions? adapterOptions = null)
    {
        var device = testAppHelper.CreateDevice(adapterOptions);

        // create server
        var server = await testAppHelper.CreateServer(socketFactory: device.SocketFactory);
        return new AppClientServerDom(testAppHelper, server, device);
    }


    public void Dispose()
    {
        App.Dispose();
        Server.Dispose();
    }
}
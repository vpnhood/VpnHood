using VpnHood.AppLib.ClientProfiles;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Server;
using VpnHood.Test;

namespace VpnHood.AppLib.Test;

public class AppClientServerDom : IDisposable
{
    public TestAppHelper TestAppHelper { get; }
    public VpnHoodServer Server { get; }
    public VpnHoodApp App { get; }
    public ClientProfile ClientProfile { get; }

    private AppClientServerDom(TestAppHelper testAppHelper, VpnHoodServer server)
    {
        TestAppHelper = testAppHelper;
        Server = server;

        // create a toke
        var token = testAppHelper.CreateAccessToken(server);

        // create app
        App = testAppHelper.CreateClientApp();
        ClientProfile = App.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        App.UserSettings.ClientProfileId = ClientProfile.ClientProfileId;
    }


    public static async Task<AppClientServerDom> Create(TestAppHelper testAppHelper)
    {
        // create server
        var server = await testAppHelper.CreateServer();
        return new AppClientServerDom(testAppHelper, server);
    }

    public void Dispose()
    {
        App.Dispose();
        Server.Dispose();
    }
}
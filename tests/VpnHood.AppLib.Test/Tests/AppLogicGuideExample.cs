using VpnHood.AppLib.Api;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.App;
using VpnHood.Core.Client.Abstractions;

namespace VpnHood.AppLib.Test.Tests;

// docs/use-the-app-logic.md, the guide "Your own screens over the app's logic", is a copy of the Snippet regions here,
// made by pub/lib/Update-Snippets.ps1; the NuGet publish fails when a copy differs. Edit a region, then run the script.
// Compiled with the tests and never run: the app starts in a UI framework's host, which hands these to its platform.
// VpnHood.App.Samples' AppLibSample.MauiForm runs the same calls.
internal static class AppLogicGuideExample
{
    internal static AppInitParams CreateInitParams(bool isDebugMode)
    {
        #region Snippet:AppLogicInit
        // who the app is and its options, which a UI framework's host hands its platform
        var initParams = new AppInitParams {
            AppId = "com.example.myvpn",
            AppName = "My VPN",
            AppVersion = new Version(1, 0, 0),
            StorageFolderName = "MyVpn", // the app's folder in the platform's place for app data
            AppOptionsFactory = context => new AppOptions(context, isDebugMode) {
                PackageTitle = "MyVpn",
                CompanyName = "Example Ltd",
                // a UI's logo and first-run consent, from its assets; your screens may skip them
                LogoAssetPath = "images/logo.png",
                PrivacyConsentAssetName = "privacy-consent",
                // VpnHood's test server: your app uses your own server's access key
                AccessKeys = [ClientOptions.TestAccessKey]
            }
        };
        #endregion

        return initParams;
    }

    internal static async Task ConnectOrDisconnect(VpnHoodApi api, CancellationToken cancellationToken)
    {
        #region Snippet:AppLogicConnect
        // the app's state, as every VpnHood UI reads it; the API raises no events,
        // so a screen reads it often
        var state = await api.App.GetState(cancellationToken);
        Console.WriteLine($"{state.ConnectionState}, last error: {state.LastError?.Message}");

        // Connect or Disconnect, whichever the state allows
        if (state.CanConnect)
            await api.App.Connect(vpnProfileId: null, serverLocation: null, ConnectPlanId.Normal,
                cancellationToken);
        else if (state.CanDisconnect)
            await api.App.Disconnect(cancellationToken);
        #endregion
    }
}

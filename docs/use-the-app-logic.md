# Your Own Screens over the App's Logic

The app's logic is a whole VPN app without its screens: it keeps the access keys, connects and reconnects when the
network changes, splits the tunnel, shows notifications, and, when you set them up, ads and billing. VpnHood's own
apps put their UI over it; this guide puts yours, which talks to it through the app's API, the same calls every
VpnHood UI makes. Where it sits among the layers is in [VpnHood Client Architecture](client-architecture.md).

For an app with VpnHood's own screens too, start from the template instead: [Build your VPN app](build-your-app.md).

## Start the app

The app starts from who it is and its options, which your UI framework's host hands its platform:

```csharp Snippet:AppLogicInit
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
```

1. **The host starts it.** In MAUI, `VpnHoodAppMaui.Init(initParams)` in `MauiProgram`, from the
   `VpnHood.AppHosting.Maui` package; the app then runs in your app's process, and the VPN service where the
   platform runs it.
2. **The access keys are the servers it connects to.** `ClientOptions.TestAccessKey` is VpnHood's test server,
   which ends each session after a few minutes; your app comes with an access key of your own server.
3. **The rest of the options** turn on what you need: ads, billing, a premium plan, updates. Each is on the options'
   page of the API reference, `AppOptions`.

## Connect, and show the state

The API answers calls and raises no events, so a screen reads the app's state often, once a second in the sample:

```csharp Snippet:AppLogicConnect
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
```

1. **The state says what a screen may offer.** `CanConnect` and `CanDisconnect` tell which button to show, and
   `ConnectionState` where the connection is.
2. **An error stays in the state** as `LastError` until it is cleared, by `ClearLastError`.
3. **The API is the app's whole surface:** beside `api.App`, its VPN profiles and their servers (`api.VpnProfiles`),
   the account (`api.Account`), billing (`api.Billing`) and the person's proxies (`api.ProxyEndPoints`).

## The sample

[AppLibSample.MauiForm](https://github.com/vpnhood/VpnHood.App.Samples/tree/main/AppLibSample.MauiForm) runs these
calls under a MAUI page of its own, on Android and Windows.

## See also

- [Use the VPN Engine in Your Own App](use-the-engine.md) — the engine alone, when you write the app around it
- [VpnHood Client Architecture](client-architecture.md) — the layers, and the two contracts between them
- [Build your VPN app](build-your-app.md) — the whole app, VpnHood's screens included, from the template

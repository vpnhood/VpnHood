# VpnHood iOS — Connect (host app)

The Connect app is a branding variant of the Client app — identical bootstrap, its own `AppConfigs.cs`
(bundle ids `com.vpnhood.connect.ios`, App Group `group.com.vpnhood.connect.ios`, `ConnectAppResources`).
Shared iOS app code is in `Src/AppLib/VpnHood.AppLib.Ios.Common`. Building this project also builds and bundles
the extension appex (`Src/Apps/Connect.Ios.Extension`).

> Connect isn't provisioned for device yet — its bundle ids / App Group / signing profiles must be created in
> the Apple portal first. See the build/deploy doc.

**iOS engineering notes → [`/docs/ios/`](../../../docs/ios/)** — build/deploy & provisioning, architecture &
App↔Extension IPC, the 52 MB jetsam memory/throughput model, extension runtime rules.

# VpnHood iOS — Connect (host app)

The Connect app is a branding variant of the Client app — identical bootstrap, its own `AppConfigs.cs`
(bundle ids `com.vpnhood.connect.ios`, App Group `group.com.vpnhood.connect.ios`, `ConnectAppResources`).
Shared iOS app code is in `src/AppLib/VpnHood.AppLib.Ios.Common`. Building this project also builds and bundles
the extension appex (`src/Apps/Connect.Ios.Extension`).

> Connect is provisioned (since 2026-07): bundle ids + App Group are registered in the Apple portal, and the
> "VpnHood Connect AppStore" / "VpnHood Connect Extension AppStore" profiles sign the CI/TestFlight builds.
> Device (dev) builds use automatic provisioning like the Client. See the build/deploy doc.

**iOS engineering notes → [`/docs/ios/`](../../../docs/ios/)** — build/deploy & provisioning, architecture &
App↔Extension IPC, the 52 MB jetsam memory/throughput model, extension runtime rules.

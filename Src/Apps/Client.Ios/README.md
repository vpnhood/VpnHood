# VpnHood iOS — Client (host app)

Thin host shell for the iOS Client app: `AppConfigs.cs` (product config — bundle ids, App Group, web-UI port,
resources) plus the `AppDelegate` / `SceneDelegate` / `Main` bootstrap. The shared iOS app code (the WKWebView
SPA host, `VpnHoodIosApp`) lives in `Src/AppLib/VpnHood.AppLib.Ios.Common`; shared client resources in
`Src/Apps/Client`. Building this project also builds and bundles the extension appex
(`Src/Apps/Client.Ios.Extension`).

**iOS engineering notes → [`/docs/ios/`](../../../docs/ios/)** — build/deploy & provisioning, architecture &
App↔Extension IPC, the 52 MB jetsam memory/throughput model, extension runtime rules.

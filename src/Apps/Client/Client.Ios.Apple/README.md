# VpnHood iOS — Client (host app)

Thin host shell for the iOS Client app: the `AppDelegate` / `SceneDelegate` / `Main` bootstrap, which
states the storage folder every shipped build has used and loads the product's settings
(`ClientAppConfigs`). Its bundle id, name, App Group and extension bundle id come from the product's
identity (`../Directory.Build.props`), which the build turns into the bundle, the entitlements and
`AppConstants` (see `docs/source-layout.md`). The shared iOS app code (the WKWebView
SPA host, `VpnHoodIosApp`) lives in `src/AppLib/VpnHood.AppLib.App.Ios`; shared client resources in
`src/Apps/Client`. Building this project also builds and bundles the extension appex
(`src/Apps/Client/Client.Ios.Extension`).

**iOS engineering notes → [`/docs/ios/`](../../../docs/ios/)** — build/deploy & provisioning, architecture &
App↔Extension IPC, the 52 MB jetsam memory/throughput model, extension runtime rules.

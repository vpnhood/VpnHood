# VpnHood iOS — Client (host app)

Thin host shell for the Client app on iOS and the Apple TV: the `App` (`Main`) and `AppDelegate` bootstrap,
which states the storage folder every shipped build has used and loads the product's settings
(`ClientAppConfigs`). Its bundle id, name, App Group and extension bundle id come from the product's
identity (`../Directory.Build.props`), which the build turns into the bundle, the entitlements and
`AppConstants` (see `docs/source-layout.md`). The shared Apple app code (`VpnHoodIosApp`) lives in
`src/AppLib/VpnHood.AppLib.App.Ios`, the Avalonia UI's host in `src/AppUi/VpnHood.AppUi.Hosting.Avalonia.Ios`,
shared client resources in `src/Apps/Client`. Building this project also builds and bundles the extension
appex (`src/Apps/Client/Client.Ios.Extension`), for the same platform.

**iOS engineering notes → [`/docs/ios/`](../../../../docs/ios/)** — build/deploy & provisioning, architecture &
App↔Extension IPC, the 52 MB jetsam memory/throughput model, extension runtime rules.

## The Apple TV

The Apple TV is this project's second target, `net11.0-tvos`: the same bundle id, App Group and extension
id (`com.vpnhood.client.ios`), so both are one App Store record (universal purchase). What tvOS lacks stays
out: no file or link opens the app (`Info.tvOS.plist`, the target's manifest), and there is no in-app rating
prompt (`#if TVOS`). The UI takes its TV layout (`IosDevice.IsTv`).

The Apple libraries target tvOS beside iOS, so a build of the Apple projects needs the `tvos` workload beside
`ios`.

### Build for the Apple TV simulator (on the Mac)

```bash
~/.dotnet/dotnet build src/Apps/Client/Client.Ios.Apple/VpnHood.App.Client.Ios.Apple.csproj \
  -f net11.0-tvos -c Debug -r tvossimulator-arm64 -p:ValidateXcodeVersion=false \
  -p:AutomaticProvisioning=false -p:CodesignKey=-
xcrun simctl install <udid> src/Apps/Client/Client.Ios.Apple/bin/Debug/net11.0-tvos/tvossimulator-arm64/VpnHoodClient.app
xcrun simctl launch <udid> com.vpnhood.client.ios
```

A simulator build is signed ad hoc (`CodesignKey=-`): it needs no provisioning profile. The
simulator runs no Network Extension, so Connect ends with `NEVPNErrorDomain Code=5 "IPC failed"`.
A device build needs tvOS provisioning profiles for both bundle ids, which do not exist yet.

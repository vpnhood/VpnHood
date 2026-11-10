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
  -f net11.0-tvos -c Debug -r tvossimulator-arm64 \
  -p:AutomaticProvisioning=false -p:CodesignKey=-
xcrun simctl install <udid> src/Apps/Client/Client.Ios.Apple/bin/Debug/net11.0-tvos/tvossimulator-arm64/VpnHoodClient.app
xcrun simctl launch <udid> com.vpnhood.client.ios
```

A simulator build is signed ad hoc (`CodesignKey=-`): it needs no provisioning profile. The
simulator runs no Network Extension, so Connect ends with `NEVPNErrorDomain Code=5 "IPC failed"`.

### Build for an Apple TV

1. Pair it with Xcode: on the Apple TV, Settings › Remotes and Devices › Remote App and Devices; on the Mac,
   Xcode › Window › Devices and Simulators › the Apple TV › Pair, then the code the TV shows. An Apple TV 4K
   has no USB port: it works over the network.
2. Register it in the developer account and make **tvOS App Development** profiles for both bundle ids (the
   app and `….networkextension`), from an Apple Development certificate, with the Apple TV in them. The App
   IDs' capabilities are the iPhone's, so the profiles carry the packet tunnel and the App Group. Install
   them on the Mac (`~/Library/Developer/Xcode/UserData/Provisioning Profiles/<UUID>.mobileprovision`).
3. Build Release, as on the iPhone (Debug's AOT output does not fit the extension's memory), from a session
   whose keychain is unlocked, then install with `devicectl`:

```bash
~/.dotnet/dotnet build src/Apps/Client/Client.Ios.Apple/VpnHood.App.Client.Ios.Apple.csproj \
  -f net11.0-tvos -r tvos-arm64 -c Release -p:ArchiveOnBuild=false -p:_DeviceName=:v2:udid=<udid> \
  -p:SolutionDir="$(pwd)/"
xcrun devicectl device install app --device <udid> src/Apps/Client/Client.Ios.Apple/bin/Release/net11.0-tvos/tvos-arm64/VpnHoodClient.app
xcrun devicectl device capture screenshot --device <udid> --destination tv.png
```

The first Connect asks to add the VPN configuration (Allow, with the remote). The extension's log is in the
App Group's `Library/Caches/vpn-service-config/vpn.log` (`devicectl device copy from --domain-type
appGroupDataContainer`), its memory probe in its own container's `Library/Caches/Documents/ext-mem.log`.
The extension's memory ceiling on tvOS is the iPhone's, 50 MB (footprint plus `os_proc_available_memory`).

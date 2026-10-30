# VpnHood Apple TV — Client (host app)

The Client app on an Apple TV: the iOS app's other platform. It has the same bundle id, App Group
and extension id (`com.vpnhood.client.ios`), so both are one App Store record (universal purchase).
The code is the iOS head's (`Client.Ios.Apple`), less what tvOS lacks: no file or link opens the
app, and there is no in-app rating prompt. The UI takes its TV layout (`IosDevice.IsTv`). Building
this project also builds and bundles the extension (`Client.Tvos.Extension`).

Not released yet. The Apple libraries target tvOS only for a build that asks for it, so every
build passes `-p:VhAppleTv=true` and needs the `tvos` workload. The project is not in
`VpnHood.slnx`, since a restore of the solution would need that workload too.

## Build for the Apple TV simulator (on the Mac)

```bash
~/.dotnet/dotnet build src/Apps/Client/Client.Tvos.Apple/VpnHood.App.Client.Tvos.Apple.csproj \
  -c Debug -r tvossimulator-arm64 -p:VhAppleTv=true -p:ValidateXcodeVersion=false \
  -p:AutomaticProvisioning=false -p:CodesignKey=-
xcrun simctl install <udid> src/Apps/Client/Client.Tvos.Apple/bin/Debug/net11.0-tvos/tvossimulator-arm64/VpnHoodClient.app
xcrun simctl launch <udid> com.vpnhood.client.ios
```

A simulator build is signed ad hoc (`CodesignKey=-`): it needs no provisioning profile. The
simulator runs no Network Extension, so Connect ends with `NEVPNErrorDomain Code=5 "IPC failed"`.
A device build needs tvOS provisioning profiles for both bundle ids, which do not exist yet.

**iOS engineering notes → [`/docs/ios/`](../../../../docs/ios/)**: the extension runs the same code.

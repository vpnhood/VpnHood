# VpnHood iOS — Connect (host app)

The Connect app is a branding variant of the Client app — identical bootstrap, its own identity
(bundle ids `com.vpnhood.connect.ios`, App Group `group.com.vpnhood.connect.ios`, built from
`../Directory.Build.props`; see `docs/source-layout.md`) and the product's settings
(`ConnectAppConfigs`) and premium features (`ConnectAppResources`).
Shared iOS app code is in `src/AppLib/VpnHood.AppLib.App.Ios`. Building this project also builds and bundles
the extension appex (`src/Apps/Connect/Connect.Ios.Extension`).

> Connect is provisioned (since 2026-07): bundle ids + App Group are registered in the Apple portal, and the
> "VpnHood Connect AppStore" / "VpnHood Connect Extension AppStore" profiles sign the CI/TestFlight builds.
> Device (dev) builds use automatic provisioning like the Client. See the build/deploy doc.

**iOS engineering notes → [`/docs/ios/`](../../../../docs/ios/)** — build/deploy & provisioning, architecture &
App↔Extension IPC, the 52 MB jetsam memory/throughput model, extension runtime rules.

## The Apple TV

The Apple TV is this project's second target, `net11.0-tvos`: the same bundle id, App Group and extension
id (`com.vpnhood.connect.ios`), so both are one App Store record (universal purchase), its subscriptions
included. Sign in with Apple and StoreKit billing work as on iOS. What tvOS lacks stays out: there is no
in-app rating prompt (`#if TVOS`), and no subscription-management sheet, so the account page sends the person
to another of their devices (`AppStoreBillingProvider`). The UI takes its TV layout (`IosDevice.IsTv`).

The Apple libraries target tvOS beside iOS, so a build of the Apple projects needs the `tvos` workload beside
`ios`.

### Build for the Apple TV simulator (on the Mac)

```bash
~/.dotnet/dotnet build src/Apps/Connect/Connect.Ios.Apple/VpnHood.App.Connect.Ios.Apple.csproj \
  -f net11.0-tvos -c Debug -r tvossimulator-arm64 -p:ValidateXcodeVersion=false \
  -p:AutomaticProvisioning=false -p:CodesignKey=-
xcrun simctl install <udid> src/Apps/Connect/Connect.Ios.Apple/bin/Debug/net11.0-tvos/tvossimulator-arm64/VpnHoodConnect.app
xcrun simctl launch <udid> com.vpnhood.connect.ios
```

A simulator build is signed ad hoc (`CodesignKey=-`): it needs no provisioning profile. The
simulator runs no Network Extension, so a connect ends with `NEVPNErrorDomain Code=5 "IPC failed"`.
A device build is CLIENT's (`../../Client/Client.Ios.Apple/README.md`, "Build for an Apple TV") with this
project and `VpnHoodConnect.app`; its tvOS development profiles also carry Sign in with Apple.

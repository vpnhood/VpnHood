# Build, Deploy & Provisioning — VpnHood iOS (Client & Connect)

How to build the App + Network Extension, install/run on device, stream logs, and fix signing/provisioning.

> **Framework note:** the target is **`net11.0-ios` / CoreCLR** — this is what fixed the jetsam crash and
> proxy-mode throughput. Build with **`~/.dotnet11/dotnet`** (the system `dotnet` can't target `net11.0-ios`).
> .NET 11 is still a **preview SDK** (11.0.100-preview.5) — a shippable/App-Store build needs .NET 11 GA. See
> [ios-extension-memory-and-throughput.md](ios-extension-memory-and-throughput.md) for the runtime/memory rationale.

## Project layout (in this monorepo)
The iOS apps live under `Src/Apps/` — one host project + one Network-Extension (`.appex`) project each:

| App | Host csproj | Extension csproj | App bundle id |
|-----|-------------|------------------|---------------|
| Client  | `Src/Apps/Client.Ios/VpnHood.App.Client.Ios.csproj`   | `Src/Apps/Client.Ios.Extension/…`   | `com.vpnhood.client.ios` |
| Connect | `Src/Apps/Connect.Ios/VpnHood.App.Connect.Ios.csproj` | `Src/Apps/Connect.Ios.Extension/…`  | `com.vpnhood.connect.ios` |

The host references the extension as an `IsAppExtension` `ProjectReference`, so **building the host also builds
and bundles the appex**. All iOS build settings are inlined per-csproj (no shared props file).

## Prerequisites
- macOS + Xcode (see the framework note; the `ValidateXcodeVersion=false` override in each csproj allows a newer
  Xcode than the preview workload pins — a build warning VPNHOOD0001 fires when the override can be removed).
- Physical iPhone registered in the Apple Developer account (team `6KKW3MKLR7`, OmegaHood LLC) with, for **both**
  bundle IDs of the app you're building: its App Group enabled + the Network Extension capability enabled.
- `AutomaticProvisioning = true` (Xcode picks cert/profile).
- All commands below run **from the monorepo root** and take the device UDID from a shell var:
  ```bash
  DEVICE=<your-device-udid>          # list devices: xcrun devicectl list devices
  ```

## Build (always Release for device)
Debug AOT emits ~51 MB and hits the 52 MB jetsam limit — **always build Release** for the device.
```bash
# Client (swap Client.Ios -> Connect.Ios for the Connect app)
rm -rf Src/Apps/Client.Ios/bin Src/Apps/Client.Ios/obj \
       Src/Apps/Client.Ios.Extension/bin Src/Apps/Client.Ios.Extension/obj   # clean: avoid stale AOT
~/.dotnet11/dotnet build Src/Apps/Client.Ios/VpnHood.App.Client.Ios.csproj \
  -f net11.0-ios -r ios-arm64 -c Release \
  -p:ArchiveOnBuild=false \
  -p:_DeviceName=:v2:udid=$DEVICE \
  -p:SolutionDir="$(pwd)/"
```
- `-p:SolutionDir="$(pwd)/"` (trailing slash **required**) is mandatory in Release — without it the core `.csproj`
  files emit `CS8101: pathmap incorrectly formatted` (the `PathMap` in `Src/Directory.Build.props` needs it).
- Repo uses `.slnx`; build the host csproj directly. The host build also builds the Extension appex.
- Output: `Src/Apps/Client.Ios/bin/Release/net11.0-ios/ios-arm64/VpnHood.App.Client.Ios.app`
  (contains `PlugIns/VpnHood.App.Client.Ios.Extension.appex`).

## Deploy & run (devicectl)
```bash
APP=Src/Apps/Client.Ios/bin/Release/net11.0-ios/ios-arm64/VpnHood.App.Client.Ios.app
xcrun devicectl device install app     --device $DEVICE "$APP"
xcrun devicectl device process launch  --device $DEVICE com.vpnhood.client.ios
```

## Streaming logs
`devicectl device` has **no `syslog`** subcommand. The **App** uses a console logger (readable stdout) — attach with `--console`:
```bash
xcrun devicectl device process launch --device $DEVICE --terminate-existing --console com.vpnhood.client.ios
```
The console attach stops once the app backgrounds. For the **Extension** (stdout → /dev/null) and probe/footprint
data, pull its container:
```bash
xcrun devicectl device copy from --device $DEVICE \
  --domain-type appDataContainer --domain-identifier com.vpnhood.client.ios.networkextension \
  --source Documents --destination .working/pulled
# com.vpnhood.client.ios  → the App's own container
```
- Device clock logs in **UTC**.
- **No logs in repo root** — always write/copy diagnostic files (`ext-mem.log`, `ext-route-dump.txt`, build logs)
  to `.working/` (or `logs/`), never the project root.
- Device crash/jetsam log (needs sudo): `sudo /usr/bin/log collect --device-udid $DEVICE --last 10m --output /tmp/d.logarchive`

## Provisioning / signing
Known-good for **Client** (team `6KKW3MKLR7`, OmegaHood LLC): profiles `"VpnHood Client Dev Profile"` (App) +
`"VpnHood Client Extension Dev Profile"` (Extension). **Connect** needs its own equivalents created before it can
build for device (bundle ids `com.vpnhood.connect.ios` / `.networkextension`, App Group
`group.com.vpnhood.connect.ios`, and matching profiles) — see below.

### Diagnose a stale profile
```bash
APP=Src/Apps/Client.Ios/bin/Release/net11.0-ios/ios-arm64/VpnHood.App.Client.Ios.app
# what the embedded profile allows
security cms -D -i "$APP/embedded.mobileprovision" \
  | plutil -convert xml1 - -o - | grep -A3 -E "ProvisionedDevices|application-groups|TeamIdentifier"
# what entitlements the signed binary actually claims
codesign -d --entitlements :- "$APP" \
  | plutil -convert xml1 - -o - | grep -A3 -E "application-groups|network.extension"
```
**Critical:** if the binary claims `application-groups: group.com.vpnhood.client.ios` but the profile shows
`<array/>` (empty), iOS rejects the install with `0xe8008015 / ApplicationVerificationFailed`. The profile must
grant every entitlement the binary claims.

### Fix missing device / App Group (and set up Connect)
At [developer.apple.com](https://developer.apple.com) (OmegaHood LLC, team `6KKW3MKLR7`) — substitute
`client`→`connect` for the Connect app:
1. **App Groups** → create `group.com.vpnhood.client.ios` if missing.
2. **Identifiers** → `com.vpnhood.client.ios`: enable App Groups (add the group) + confirm Network Extensions.
3. **Identifiers** → `com.vpnhood.client.ios.networkextension`: same (App Groups + the group).
4. **Profiles** → regenerate the App + Extension dev profiles → download → copy the `.mobileprovision` files to
   `~/Library/MobileDevice/Provisioning Profiles/` (named by UUID).
5. Rebuild and re-verify with the `codesign`/`security cms` commands above (the group must appear in **both** the
   signed binary and the embedded profile).

# Build, Deploy & Provisioning — VpnHood iOS (Client & Connect)

How to build the App + Network Extension, install/run on device, stream logs, and fix signing/provisioning.

> **Framework note:** the target is **`net11.0-ios` / CoreCLR** — this is what fixed the jetsam crash and
> proxy-mode throughput. Build with **`~/.dotnet11/dotnet`** (the system `dotnet` can't target `net11.0-ios`).
> .NET 11 is still a **preview SDK** (11.0.100-preview.7) — a shippable/App-Store build needs .NET 11 GA. See
> [ios-extension-memory-and-throughput.md](ios-extension-memory-and-throughput.md) for the runtime/memory rationale.

## Project layout (in this monorepo)
The iOS apps live under `src/Apps/` — one host project + one Network-Extension (`.appex`) project each:

| App | Host csproj | Extension csproj | App bundle id |
|-----|-------------|------------------|---------------|
| Client  | `src/Apps/Client.Ios/VpnHood.App.Client.Ios.csproj`   | `src/Apps/Client.Ios.Extension/…`   | `com.vpnhood.client.ios` |
| Connect | `src/Apps/Connect.Ios/VpnHood.App.Connect.Ios.csproj` | `src/Apps/Connect.Ios.Extension/…`  | `com.vpnhood.connect.ios` |

The host references the extension as an `IsAppExtension` `ProjectReference`, so **building the host also builds
and bundles the appex**. All iOS build settings are inlined per-csproj (no shared props file).

## Prerequisites
- macOS + Xcode matching the workload's recommended version (currently Xcode 26.6 with preview.7 — no
  version override needed; if a future workload pins an older Xcode again, set `ValidateXcodeVersion=false`
  per csproj as a temporary bridge).
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
rm -rf src/Apps/Client.Ios/bin src/Apps/Client.Ios/obj \
       src/Apps/Client.Ios.Extension/bin src/Apps/Client.Ios.Extension/obj   # clean: avoid stale AOT
~/.dotnet11/dotnet build src/Apps/Client.Ios/VpnHood.App.Client.Ios.csproj \
  -f net11.0-ios -r ios-arm64 -c Release \
  -p:ArchiveOnBuild=false \
  -p:_DeviceName=:v2:udid=$DEVICE \
  -p:SolutionDir="$(pwd)/"
```
- `-p:SolutionDir="$(pwd)/"` (trailing slash **required**) is mandatory in Release — without it the core `.csproj`
  files emit `CS8101: pathmap incorrectly formatted` (the `PathMap` in the root `Directory.Build.props` needs it).
- Repo uses `.slnx`; build the host csproj directly. The host build also builds the Extension appex.
- Output: `src/Apps/Client.Ios/bin/Release/net11.0-ios/ios-arm64/VpnHood.App.Client.Ios.app`
  (contains `PlugIns/VpnHood.App.Client.Ios.Extension.appex`).

## Deploy & run (devicectl)
```bash
APP=src/Apps/Client.Ios/bin/Release/net11.0-ios/ios-arm64/VpnHood.App.Client.Ios.app
xcrun devicectl device install app     --device $DEVICE "$APP"
xcrun devicectl device process launch  --device $DEVICE com.vpnhood.client.ios
```

## Run on a Mac (Apple silicon) — no iPhone needed
The stock `ios-arm64` Release build runs natively on an Apple-silicon Mac ("iPad app on Mac") — same
binary, same NE appex (macOS supports packet-tunnel **app** extensions only via this route; a native
Mac app would need a system extension). Verified working 2026-08-24 (M2 Pro, Client app). One-time setup:
1. Register the Mac as a **macOS** device in the portal — its **Provisioning UDID** is in
   About This Mac → System Report → Hardware.
2. Add the Mac to **both** dev profiles (App + Extension): Profiles → Edit → tick the Mac → Save →
   Download (profiles are never retrofitted — regenerating is required; **don't rename**, renames mint a
   new identity). Install the downloads to `~/Library/MobileDevice/Provisioning Profiles/<UUID>.mobileprovision`
   and delete the stale same-name copies so the build can't pick an old one.

Then build exactly as above (no `_DeviceName` needed). macOS refuses to launch a raw iOS `.app`
(“incorrect executable format”) — it must sit in the wrapper bundle Xcode/the App Store normally create:
```bash
APP=src/Apps/Client.Ios/bin/Release/net11.0-ios/ios-arm64/VpnHood.App.Client.Ios.app
WRAP=".working/mac-run/VpnHood Client.app"
rm -rf .working/mac-run && mkdir -p "$WRAP/Wrapper"
cp -R "$APP" "$WRAP/Wrapper/"
ln -s "Wrapper/VpnHood.App.Client.Ios.app" "$WRAP/WrappedBundle"
open "$WRAP"
```
- Verify first with the "Diagnose a stale profile" commands below — both `embedded.mobileprovision`
  files must list the Mac's provisioning UDID or the launch is refused.
- The VPN approval prompt appears in **System Settings → VPN**; extension logs are readable live in
  Console.app (no container-pull needed).
- **Dev-signed limit — the packet-tunnel extension will NOT spawn** (verified 2026-08-24): the app
  runs, the NE config saves, approval works, `StartVpnTunnel` is accepted — then launchd's spawn of
  the `.appex` is vetoed by AMFI with `-413 No matching profile found` (watch it live:
  `/usr/bin/log stream --predicate 'process IN {"amfid","neagent"}'` — note `/usr/bin/log`, since
  zsh shadows `log` with a builtin that silently no-ops). The appex needs its iOS provisioning
  profile **installed in misagent's store** (`/var/db/MobileIdentityService/Profiles`), which is
  SIP-protected; only Xcode's deploy step or App Store/TestFlight installs can write it —
  `profiles install -type=provisioning` rejects iOS profiles (-214) and `devicectl` can't target
  the local Mac. So a dev-signed Mac run validates UI/billing/SIWA/approval only; test the actual
  tunnel on macOS with a **TestFlight for Mac** build (store-signed, no dev profiles involved).
- macOS has no ~52 MB jetsam ceiling — a build that runs here can still die on device; keep tuning
  against the iOS limit.
- App Store availability on Mac is a separate, server-side switch (App Store Connect → Pricing and
  Availability → Apple silicon Mac availability); nothing in the build changes for it.

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
`"VpnHood Client Extension Dev Profile"` (Extension). **Connect** is provisioned too (since 2026-07): bundle ids
`com.vpnhood.connect.ios` / `.networkextension` and App Group `group.com.vpnhood.connect.ios` are registered,
and the App Store profiles `"VpnHood Connect AppStore"` / `"VpnHood Connect Extension AppStore"` sign the
CI/TestFlight builds (`.user/VpnHoodConnect/ios/`). For device (dev) builds Connect relies on automatic
provisioning; create named dev profiles per the steps below only if that isn't available.

### Diagnose a stale profile
```bash
APP=src/Apps/Client.Ios/bin/Release/net11.0-ios/ios-arm64/VpnHood.App.Client.Ios.app
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

## Enable in-app purchase (Connect-style apps; white-label/fork checklist)
Everything a branded app needs so StoreKit purchases reach the account portal (WHMCS `vpnhoodiap`) and
come back as provisioned subscriptions. Substitute your own bundle id, team and portal host throughout.
Learned the hard way on 2026-08-23 — the quirks called out below are all real.

### 1. App Store Connect — products
- Create the auto-renewable subscriptions (one subscription group). The **product id IS the plan+cycle**
  (`vpnhood_1_month_subscription`, `vpnhood_1_year_subscription`); it must match the portal's catalog
  mapping (store `appstore` + your bundle id) EXACTLY — the app carries no fallback ids.
- Each subscription needs a price, at least one localization, and an **App Review screenshot**, or it sits
  in `MISSING_METADATA` and cannot be sold. `.github/scripts/asc-iap.mjs report` audits all of this.

### 2. App ID capabilities (developer portal)
- `IN_APP_PURCHASE` is on by default; accounts also need **Sign in with Apple** on the App ID.
- **Changing capabilities INVALIDATES every existing profile on that App ID** (dev + App Store; extension
  profiles on the other App ID survive). Regenerate profiles and refresh the CI signing secret
  (`IOS_PROVISION_APP_BASE64`) afterwards — Apple never retrofits a profile in place.
- **API-enable quirk:** enabling Sign in with Apple via the ASC API can leave a half-propagated record —
  everything reads enabled, profiles even mint with the entitlement, but real sign-ins fail with Apple's
  "Sign Up Not Completed" alert (surfaces as `ASAuthorizationError 1001`). Fix: uncheck + save + re-check
  the capability in the **portal UI** (then regenerate profiles again). Always verify with a real
  on-device sign-in before calling it done.
- Renaming a profile in the portal UI **regenerates it** (new UUID) — re-download and reinstall.

### 3. Portal credentials — use a scoped key
- Generate an **In-App Purchase key** (App Store Connect → Users and Access → Integrations →
  In-App Purchase), NOT a full App Store Connect API key: it can only call the App Store Server API
  (validate transactions / read subscription status), so a portal-server compromise cannot touch
  certificates, profiles or apps. It is team-wide — Apple has no per-app scoping.
- Store it on the portal's app row (encrypted) as
  `{ "issuerId": "…", "keyId": "…", "privateKey": "-----BEGIN PRIVATE KEY-----…" }`.
- **Unpublished-app quirk (handled in vpnhoodiap ≥ the 2026-08 fix):** for an app that has never been
  published to the App Store (TestFlight-only), the **production** App Store Server API answers a bare
  `401` (empty body) even with valid credentials; the sandbox host must be retried on 401 as well as 404.

### 4. Server notifications (no Pub/Sub — Apple posts directly)
App Store Connect → the app → App Information → **App Store Server Notifications** (V2): set the
Sandbox and Production URLs to the portal webhook —
`https://<portal-host>/modules/addons/vpnhoodiap/webhook.php?store=appstore&t=<app row's webhook_token>`.
Notifications are the freshness channel only (renewals/refunds/grace); purchases complete without them
via `POST /v1/billing/purchases` + the daily reconciliation.

### 5. Sandbox testing
- Create a **sandbox tester** (Users and Access → Sandbox). On the device, sign it in under
  **Settings → Developer → Sandbox Apple Account** (moved from Settings → App Store on modern iOS).
  Purchases in dev-signed builds then bill that tester automatically — Sign in with Apple, by contrast,
  always uses the phone's real Apple ID; the two never mix.
- **Sandbox clocks are compressed:** a 1-month subscription renews every 5 minutes, up to 12 times, then
  the whole lineage is `expired` (~1 h). A proof validated after that is correctly refused
  (`purchase_inactive`) — buy again instead of debugging.
- A stuck tester ("already subscribed", expired lineages) is reset by **clearing its purchase history**
  (ASC UI, or API `POST /v2/sandboxTestersClearPurchaseHistoryRequest`); the device app then needs an
  uninstall/reinstall to drop its cached StoreKit state.
- A brand-new sandbox transaction can briefly answer `Transaction id not found` on the server API —
  retry the purchase/restore before suspecting configuration.

### 6. The StoreKit facade
Billing goes through `VpnHoodStoreKit.xcframework` (Swift, `src/AppLib/VpnHood.AppLib.Ios.AppStore/swift/`).
It is committed so CI needs no Swift toolchain; rebuild with `./build-xcframework.sh` only when
`StoreKitBridge.swift` changes, and verify the four `vhsk_*` symbols with
`nm -gU …/VpnHoodStoreKit.framework/VpnHoodStoreKit | grep vhsk`.

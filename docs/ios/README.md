# VpnHood iOS — engineering notes

Index for the iOS Client & Connect apps and their Network Extension. The apps are thin: they live in
`src/Apps/{Client,Connect}/{Client,Connect}.Ios.Apple` (host) + `….Ios.Extension` (`.appex`), and the real
device/extension/TUN/TCP-stack implementation is in `src/Core/*`
(`VpnHood.Core.Client.Devices.Ios`, `VpnHood.Net.VpnAdapters.IosTun`, `VpnHood.Net.TcpStack`,
`VpnHood.Net.Quic.Ios`). Client and Connect share all of it — Connect is just a branding variant whose
ids and name come from its product's identity (`src/Apps/Connect/Directory.Build.props`, see
[source-layout](../source-layout.md#the-apps-identity)) and whose product settings live in `ConnectAppConfigs`.

## Topics — read the relevant one before working in that area
- **Build / deploy / provisioning** → [build-deploy-and-provisioning.md](build-deploy-and-provisioning.md)
- **Architecture & App↔Extension IPC** → [architecture-and-ipc.md](architecture-and-ipc.md)
- **Memory & throughput** (52 MB jetsam, proxy mode, .NET 11/CoreCLR) → [ios-extension-memory-and-throughput.md](ios-extension-memory-and-throughput.md)
  — **read before changing anything memory-/throughput-/TCP-stack-related.**
- **Extension runtime rules & gotchas** → [extension-runtime-rules.md](extension-runtime-rules.md)

## Always-on iOS rules
- **Build Release for the device** (Debug AOT ≈51 MB hits the ~52 MB jetsam limit).
- **Built with .NET 11 / CoreCLR** (TFM `net11.0-ios`, via `~/.dotnet11/dotnet` — the system `dotnet` can't
  target it). This is what fixed the jetsam crash — see the memory doc. net11 is still a preview SDK; a
  shippable App-Store build needs net11 GA.
- **Diagnostic logs → `.working/` (or `logs/`), never the repo root.**
- **Don't commit a test access key** in any head — production has none in code (Connect's comes from its
  embedded key file, Client's is added via the UI).

## Identity & provisioning
Team `6KKW3MKLR7` (OmegaHood LLC). Namespaces `VpnHood.App.Client.Ios.Apple` / `VpnHood.App.Connect.Ios.Apple`, each matching its project.
The build makes all three ids from the product's id base (`<base>.ios`, `<base>.ios.networkextension`,
`group.<base>.ios`): the bundles, the App Group in both entitlements, and the `AppConstants` the app reads.

| App | App bundle id | Extension bundle id | App Group | Dev profiles |
|-----|---------------|---------------------|-----------|--------------|
| Client  | `com.vpnhood.client.ios`  | `com.vpnhood.client.ios.networkextension`  | `group.com.vpnhood.client.ios`  | "VpnHood Client Dev Profile" + "VpnHood Client Extension Dev Profile" |
| Connect | `com.vpnhood.connect.ios` | `com.vpnhood.connect.ios.networkextension` | `group.com.vpnhood.connect.ios` | *(automatic provisioning — App IDs/App Group registered since 2026-07; App Store profiles "VpnHood Connect AppStore" + "VpnHood Connect Extension AppStore" sign CI/TestFlight builds)* |

See [build-deploy-and-provisioning.md](build-deploy-and-provisioning.md) for the portal steps.

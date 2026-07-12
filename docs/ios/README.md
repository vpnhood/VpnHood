# VpnHood iOS — engineering notes

Index for the iOS Client & Connect apps and their Network Extension. The apps are thin: they live in
`src/Apps/{Client,Connect}.Ios` (host) + `…{Client,Connect}.Ios.Extension` (`.appex`), and the real
device/extension/TUN/TCP-stack implementation is in `src/Core/*`
(`VpnHood.Core.Client.Devices.Ios`, `VpnHood.Core.VpnAdapters.IosTun`, `VpnHood.Core.TcpStack`,
`VpnHood.Core.Quic.Ios`). Client and Connect share all of it — Connect is just a branding variant whose
per-product values live in its `AppConfigs.cs`.

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
- **Don't commit a test `AccessKey`** in any `AppConfigs.cs` — production defaults to `null` (key added via UI).

## Identity & provisioning
Team `6KKW3MKLR7` (OmegaHood LLC). Namespaces `VpnHood.App.Client.Ios` / `VpnHood.App.Connect.Ios`.

| App | App bundle id | Extension bundle id | App Group | Dev profiles |
|-----|---------------|---------------------|-----------|--------------|
| Client  | `com.vpnhood.client.ios`  | `com.vpnhood.client.ios.networkextension`  | `group.com.vpnhood.client.ios`  | "VpnHood Client Dev Profile" + "VpnHood Client Extension Dev Profile" |
| Connect | `com.vpnhood.connect.ios` | `com.vpnhood.connect.ios.networkextension` | `group.com.vpnhood.connect.ios` | *(create these — Connect isn't provisioned yet)* |

See [build-deploy-and-provisioning.md](build-deploy-and-provisioning.md) for the portal steps.

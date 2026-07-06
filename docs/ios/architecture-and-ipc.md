# Architecture & App↔Extension IPC — VpnHood iOS Client

## Two targets + the sibling core repo
| Target | Bundle ID | Purpose |
|--|--|--|
| `App/` | `com.vpnhood.client.ios` | Host UI app |
| `Extension/` | `com.vpnhood.client.ios.networkextension` | Network Extension (Packet Tunnel Provider) |

The device/extension/adapter implementations live in the **sibling core repo** `../VpnHood` (referenced via
`ProjectReference`, never copied):

| Type | Location (`../VpnHood`) | Role |
|--|--|--|
| `IosDevice` | `Src/Core/VpnHood.Core.Client.Devices.Ios/IosDevice.cs` | `IDevice`; NEVPNManager save/load/start, creates the IPC transport |
| `IosVpnService` (abstract) | same project | `NEPacketTunnelProvider` + `IVpnServiceHandler`; `StartTunnel`, `HandleAppMessage`, `StartMemoryGuard`/`StartMemoryProbe` |
| `IosMessageListener` / transport | same project | App↔Extension IPC over `SendProviderMessage` / `HandleAppMessage` |
| `IosVpnAdapter` | `Src/Core/VpnHood.Core.VpnAdapters.IosTun/` | `IVpnAdapter`; **batched** native write (`SendPacketsAsync` → `NEPacketTunnelFlow.WritePackets`), read via one-shot `ReadPackets` callback |
| `LocalTcpStack` (proxy mode) | `Src/Core/VpnHood.Core.TcpStack/` | user-space TCP stack used when `UseTcpProxy=true` |

**This host repo only contains thin glue:**
- `Extension/PacketTunnelProvider.cs` — `[Register("PacketTunnelProvider")]` subclass of core `IosVpnService`,
  with the `(NativeHandle)` ctor. The `[Register]` name **must** match `NSExtensionPrincipalClass` in
  `Extension/Info.plist`. **This subclass is required** — pointing the principal class straight at core
  `IosVpnService` crashes on launch under .NET 11/CoreCLR's registrar (see the memory/throughput doc).
- `App/AppDelegate.cs` / `App/SceneDelegate.cs` — host UI + `VpnHoodApp.Init(new IosDevice(...))`.

Both `App.csproj` and `Extension.csproj` `ProjectReference` the core `VpnHood.Core.Client.Devices.Ios` project
(which transitively brings in Host + iOSTun + TcpStack + Device).

> **Do NOT re-add local `IosDevice.cs` / `IosVpnService.cs` / `IosVpnAdapter.cs` to this repo.** They were moved
> to core; a stale local copy diverges from the core API and silently breaks IPC or re-introduces the jetsam kill.

## App Group — the IPC channel
Both targets share App Group `group.com.vpnhood.client.ios` (in each `Entitlements.plist`). The config folder
for both sides resolves to:
```
NSFileManager.DefaultManager.GetContainerUrl("group.com.vpnhood.client.ios")?.Path + "/vpn-service/"
```
If `GetContainerUrl` returns `null` (entitlement missing from the profile) it falls back to `LocalApplicationData`
— **which breaks IPC** (the two processes get different paths). Do **not** change `AppGroupId`; it must match on
both sides.

### Connect flow
1. **App** (`VpnServiceManager`) writes `vpn.config` (JSON) into the shared `vpn-service/` folder.
2. **App** (`IosDevice.StartVpnService`) saves/loads the NEVPNManager preference and starts the tunnel.
3. **Extension** (`IosVpnService.StartTunnel`) is launched by iOS, reads `vpn.config` via `VpnServiceHost.TryConnect`.
4. **Extension** runs a `VpnServiceHost` with a message-based API listener; the App sends API requests over the
   **NETunnelProvider app-message channel** (below) — not status files.

## Transport (App↔Extension request/response API)
| Interface | Side | Purpose |
|--|--|--|
| `IVpnServiceApiTransport` | App | sends an `IApiRequest`, awaits `ApiResponse<T>` |
| `IVpnServiceApiListener` | Extension | receives request bytes → `VpnServiceHost.ApiController` → response bytes |

- **App side:** `IDevice.CreateVpnServiceApiTransport()` defaults to `TcpVpnServiceApiTransport` (Android/desktop);
  **iOS overrides** it to `ProviderMessageVpnServiceApiTransport`, shipping bytes via
  `NETunnelProviderSession.SendProviderMessage`.
- **Extension side:** iOS passes a `MessageVpnServiceApiListener` into the `VpnServiceHost` ctor;
  `IosVpnService.HandleAppMessage` forwards bytes to it and returns the response via the completion handler.
- **Serialization:** `ApiTransportJsonContext` (source-generated) is the single JSON context for all transport
  types (`ApiTransportJsonContext.For<T>()`); `ConnectionInfo` is registered there too.
- **Exceptions:** `VpnServiceException` + subtypes live in the **Device** project under
  `VpnHood.Core.Client.Device.Exceptions` (import that namespace from iOS code).

All IPC is App-Group + the provider message channel — there is **no socket/XPC IPC**.

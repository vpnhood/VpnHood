# Network Extension — Runtime Rules & Gotchas

Hard-won constraints for the iOS Network Extension at runtime. (Memory, throughput, and `UseTcpProxy` are in
[ios-extension-memory-and-throughput.md](ios-extension-memory-and-throughput.md).)

## `SetTunnelNetworkSettings` — timing & completion handler
iOS kills the extension if `SetTunnelNetworkSettings` isn't called within ~30–60 s of `StartTunnel`, **and** if
`startTunnelCompletionHandler` fires *before* it succeeds. Current flow (no placeholder call):
`IosVpnAdapter.AdapterOpen` awaits `SetTunnelNetworkSettingsAsync` with the real settings, then calls
`CompleteStartTunnel` to fire the handler. Do **not** marshal onto the main thread
(`BeginInvokeOnMainThread`) — unnecessary in the provider process and can deadlock.

## Routing rules
The per-rule routing constraints (`tunnelRemoteAddress` must be a valid IP literal; `::/0` IPv6 default-route
injection only when broad v6 coverage is requested; `CanProtectSocket => false` so the server IP is excluded
from the tunnel; `ReadPackets` is one-shot and must be re-armed) are documented **inline in
`IosVpnAdapter.cs`** next to the code that enforces them — read those comments rather than duplicating here.

## Auto-connect
`SceneDelegate.WillConnect` waits 1.5 s then calls `VpnHoodApp.Instance.Connect()` on the main thread. **Do not
add more timers or button-press automation** — the existing timer is sufficient.

## Extension gotchas (do / don't)
- **`Console.SetOut(TextWriter.Null)` must be the FIRST line of the extension ctor**, before any other Console
  access. iOS extension stdout has no reader; once the kernel pipe buffer fills, `Console.WriteLine` blocks
  forever and hangs the constructor (extension never reaches `StartTunnel`, Documents folder stays empty). Use
  `System.Diagnostics.Trace.WriteLine` for extension logging.
- **`MtouchLink=SdkOnly`** in `Extension.csproj` is required. `None` → ~338 MB binary → type-load hangs; `Full`
  risks trimming needed types (`EXC_BAD_ACCESS` at startup). Do not change it. `TrimmerRoots.xml` isn't needed at
  `SdkOnly` (user/VpnHood assemblies aren't trimmed) — remove it if re-added.
- **Do not enable `IncludeAllNetworks`** — it cuts off the USB debug connection.
- **Never nest `<PropertyGroup Condition="...">`** inside another `<PropertyGroup>` (a formatter sometimes does
  this) → `MSB4004: "PropertyGroup" property is reserved`. It must be a top-level child of `<Project>`.
- Always build **Release** for device (Debug AOT ≈51 MB is borderline on the jetsam limit).

## Common runtime issues
| Symptom | Root cause | Fix |
|--|--|--|
| `GetContainerUrl` returns `null` | App Group entitlement missing from profile | Re-provision both targets (see build-deploy-and-provisioning) |
| Extension `EXC_BAD_ACCESS` at startup | trimming removed a needed type | keep `MtouchLink=SdkOnly` (don't use `None`/`Full`) |
| Extension crashes on launch: `Could not find the assembly …Devices.Ios` | CoreCLR registrar can't load the principal class's assembly | keep the local `PacketTunnelProvider` subclass (see architecture-and-ipc + memory doc) |
| `SaveToPreferences` times out | NEVPNManager needs the first-run permission prompt | accept the VPN permission dialog on device |
| Extension never reaches `StartTunnel` | bundle-ID / principal-class mismatch | verify `Info.plist NSExtensionPrincipalClass` matches the `[Register]` name |
| Extension process never starts, Documents empty (`NEVPNErrorDomain Code=1`) | `Console.WriteLine` before `Console.SetOut(...Null)` hung the ctor | move `Console.SetOut` to the first ctor line (above) |
| Build: `MSB4004 "PropertyGroup" is reserved` | nested `<PropertyGroup>` | un-nest it (above) |

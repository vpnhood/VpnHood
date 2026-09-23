# VpnHood iOS — Client Network Extension (.appex)

**This project is a one-file shim.** `PacketTunnelProvider.cs` is just a `[Register("PacketTunnelProvider")]`
subclass that roots the core assembly (required under .NET 11/CoreCLR's registrar). The real Network-Extension
implementation — `IosVpnService`, the iOS TUN adapter, the memory guard, the user-space TCP stack — lives in
`src/Core`, mainly:
- `src/Core/VpnHood.Core.Client.Devices.Ios` (IosVpnService, IosDevice, IPC)
- `src/Core/VpnHood.Core.VpnAdapters.IosTun` (IosVpnAdapter)
- `src/Core/VpnHood.Core.TcpStack` (proxy-mode TCP stack)

**iOS engineering notes → [`/docs/ios/`](../../../docs/ios/)** — read
[`ios-extension-memory-and-throughput.md`](../../../docs/ios/ios-extension-memory-and-throughput.md) **before**
changing anything memory-, throughput-, or TCP-stack-related (this runs under a ~52 MB jetsam limit).

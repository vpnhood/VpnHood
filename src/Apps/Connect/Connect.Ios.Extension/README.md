# VpnHood iOS — Connect Network Extension (.appex)

**This project is a one-file shim**, identical to the Client extension apart from its bundle id / App Group.
`PacketTunnelProvider.cs` is just a `[Register("PacketTunnelProvider")]` subclass that roots the core assembly.
The real Network-Extension implementation lives in `src/Core` (mainly `VpnHood.Core.Client.Devices.Ios`,
`VpnHood.Core.VpnAdapters.IosTun`, `VpnHood.Core.TcpStack`).

**iOS engineering notes → [`/docs/ios/`](../../../docs/ios/)** — read
[`ios-extension-memory-and-throughput.md`](../../../docs/ios/ios-extension-memory-and-throughput.md) **before**
changing anything memory-, throughput-, or TCP-stack-related (this runs under a ~52 MB jetsam limit).

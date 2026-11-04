# VpnHood iOS — Connect Network Extension (.appex)

**This project is a one-file shim,** for iOS and the Apple TV alike, identical to the Client extension apart
from its bundle id / App Group. `PacketTunnelProvider.cs` is just a `[Register("PacketTunnelProvider")]`
subclass that roots the core assembly. The real Network-Extension implementation lives in `src/Core` and
`src/Net`, mainly `VpnHood.Core.Client.VpnServices.Host.Ios` (IosVpnService), `VpnHood.Net.VpnAdapters.IosTun`
and `VpnHood.Net.TcpStack`.

The Apple TV target (`net11.0-tvos`) differs only in its csproj, as the Client
extension's does (`../../Client/Client.Ios.Extension/README.md` says how).

**iOS engineering notes → [`/docs/ios/`](../../../../docs/ios/)** — read
[`ios-extension-memory-and-throughput.md`](../../../../docs/ios/ios-extension-memory-and-throughput.md) **before**
changing anything memory-, throughput-, or TCP-stack-related (this runs under a ~52 MB jetsam limit).

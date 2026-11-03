# VpnHood iOS — Client Network Extension (.appex)

**This project is a one-file shim,** for iOS and the Apple TV alike. `PacketTunnelProvider.cs` is just a
`[Register("PacketTunnelProvider")]` subclass that roots the core assembly (required under .NET 11/CoreCLR's
registrar). The real Network-Extension implementation — `IosVpnService`, the iOS TUN adapter, the memory
guard, the user-space TCP stack — lives in `src/Core` and `src/Net`, mainly:

- `src/Core/VpnHood.Core.Client.VpnServices.Host.Ios` (IosVpnService, the memory guard, the app's channel)
- `src/Net/VpnHood.Net.VpnAdapters.IosTun` (IosVpnAdapter)
- `src/Net/VpnHood.Net.TcpStack` (proxy-mode TCP stack)

The Apple TV target (`net11.0-tvos`, with `-p:VhAppleTv=true`) differs only in its csproj: tvOS has Network
Extension from 17, its floor; the split filters' SQLite package ships no tvOS build of the library, so the
build links tvOS's own `libsqlite3`, which leaves out `sqlite3_enable_load_extension`, a call the filters
never make; and the managed-heap cap is iOS's until an Apple TV's own limit is measured.

**iOS engineering notes → [`/docs/ios/`](../../../../docs/ios/)** — read
[`ios-extension-memory-and-throughput.md`](../../../../docs/ios/ios-extension-memory-and-throughput.md) **before**
changing anything memory-, throughput-, or TCP-stack-related (this runs under a ~52 MB jetsam limit).

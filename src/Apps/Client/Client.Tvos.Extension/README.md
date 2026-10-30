# VpnHood Apple TV — Client Network Extension (.appex)

The iOS extension's one-file shim (`Client.Ios.Extension`) for tvOS: `PacketTunnelProvider.cs`
roots the core assembly, whose `IosVpnService` is the extension on both platforms. tvOS has Network
Extension from 17, the project's floor.

Two differences from iOS, both in the csproj:
1. **SQLite:** the split filters' SQLite package ships no tvOS build of the library, so the build
   links tvOS's own `libsqlite3` (`--gcc_flags=-lsqlite3`). It leaves out
   `sqlite3_enable_load_extension`, which the filters never call (`ReferenceNativeSymbol`, `Ignore`).
2. **Memory:** the managed-heap cap is the iOS extension's until an Apple TV's own limit is measured.

Built with the app: see `Client.Tvos.Apple/README.md`.

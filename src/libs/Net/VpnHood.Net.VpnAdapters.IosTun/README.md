# VpnHood.Net.VpnAdapters.IosTun

The iOS and tvOS VPN adapter of [VpnHood](https://github.com/vpnhood/VpnHood): the tunnel of an app's packet tunnel
extension, whose IP packets a VPN reads and writes through `NEPacketTunnelFlow`. It implements
`VpnHood.Net.VpnAdapters.Abstractions`.

```bash
dotnet add package VpnHood.Net.VpnAdapters.IosTun
```

## How it works

- `IosVpnAdapter` is an `IVpnAdapter`: make it in your `NEPacketTunnelProvider`'s start of the tunnel, with the
  provider, an `IosVpnAdapterSettings` and the completion handler iOS gave the start, and use it as the
  [Abstractions quick start](https://github.com/vpnhood/VpnHood/blob/develop/src/Net/VpnHood.Net.VpnAdapters.Abstractions/README.md)
  uses `NullVpnAdapter`. Each open calls the handler, once the tunnel is live or with the error of applying its
  settings; a failure before that, while the settings are built, does not reach it. Wrap the handler so only its
  first call counts, and report such an earlier failure through the same wrapper.
- A start gathers the addresses, routes, DNS servers and MTU, and applies them all at once with
  `SetTunnelNetworkSettings`. iOS hands the packets the device sends in batches, through callbacks; the adapter
  writes in batches of up to 32.
- When the network seems to have no IPv6, an IPv6 route shorter than /64 brings `::/0` and `2000::/3` into the
  tunnel, as iOS was seen to hold back AAAA lookups otherwise.
- The adapter cannot protect a socket; the extension's own sockets were seen to go around its tunnel on an Apple TV.
  There is no NAT, no metric, and no app filter, which iOS leaves to device management.
- `GetPrimaryAdapterDnsServers` gives the network's DNS servers, as last read before an open applied the tunnel's
  settings; the reading may be stale. `IosTunDiagnostics` holds traffic counters and timestamps for the extension's
  own diagnostics.

## Requirements

- iOS 15 or tvOS 17 or later, with .NET 11 for iOS or tvOS.
- A packet tunnel network extension, with the Network Extension entitlement for a packet tunnel provider, in which
  the adapter runs, within the memory limit iOS sets for an extension.

## License

LGPL-2.1-only. See the [VpnHood repository](https://github.com/vpnhood/VpnHood).

# VpnHood.Net.VpnAdapters.AndroidTun

The Android VPN adapter of [VpnHood](https://github.com/vpnhood/VpnHood): the TUN interface that Android's
`VpnService` gives an app, whose IP packets a VPN reads and writes. It implements
`VpnHood.Net.VpnAdapters.Abstractions`.

```bash
dotnet add package VpnHood.Net.VpnAdapters.AndroidTun
```

## How it works

- `AndroidVpnAdapter` is an `IVpnAdapter`: make it inside your `VpnService` with the service and an
  `AndroidVpnAdapterSettings`, and use it as the
  [Abstractions quick start](https://github.com/vpnhood/VpnHood/blob/develop/src/Net/VpnHood.Net.VpnAdapters.Abstractions/README.md)
  uses `NullVpnAdapter`.
- A start describes the interface to a `VpnService.Builder` (addresses, routes, DNS servers, MTU, session name and
  the apps to include or exclude), establishes it, and reads and writes packets through libc on its file descriptor.
- The interface stays up when the device moves to other networks, which Android handles, and takes its meteredness
  from the networks under it. There is no NAT and no metric.
- `ProtectSocket` keeps a socket, such as the one to the VPN server, out of the tunnel with `VpnService.Protect`.
- `GetPrimaryAdapterDnsServers` gives the DNS servers of the validated networks under the VPN, which .NET does not
  list on Android.

## Requirements

- Android 7.0 (API level 24) or later, with .NET 11 for Android.
- A `VpnService` in the app's manifest with the `android.permission.BIND_VPN_SERVICE` permission and an
  `android.net.VpnService` intent filter, and the app's `android.permission.ACCESS_NETWORK_STATE` permission, which
  `GetPrimaryAdapterDnsServers` needs.
- `VpnService.Prepare` checked before each connection, with the user's consent asked when it returns an intent.

## License

LGPL-2.1-only. See the [VpnHood repository](https://github.com/vpnhood/VpnHood).

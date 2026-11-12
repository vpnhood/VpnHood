# VpnHood.Net.VpnAdapters.WinDivert

A Windows VPN adapter of [VpnHood](https://github.com/vpnhood/VpnHood) on [WinDivert](https://reqrypt.org/windivert.html),
a packet capture driver, for tests and debugging. It has no interface of its own: it takes the host's outbound
packets to the routed networks before they leave, and puts the VPN's packets back in as inbound. It implements
`VpnHood.Net.VpnAdapters.Abstractions`; a Windows app runs on `VpnHood.Net.VpnAdapters.WinTun`.

```bash
dotnet add package VpnHood.Net.VpnAdapters.WinDivert
```

## How it works

- `WinDivertVpnAdapter` is an `IVpnAdapter`: make it with a `WinDivertVpnAdapterSettings` and use it as the
  [Abstractions quick start](https://github.com/vpnhood/VpnHood/blob/develop/src/Net/VpnHood.Net.VpnAdapters.Abstractions/README.md)
  uses `NullVpnAdapter`, except that it writes no packet before it has captured one.
- A captured packet's source becomes the adapter's virtual address of its IP version, if it has one; a packet the
  VPN sends back goes in addressed to the host's primary address.
- The host's DNS settings stay as they are. With `SimulateDns`, the default, every outbound UDP packet to port 53,
  wherever it goes, is captured, but loopback ones and protected ones, and sent to one of the adapter's DNS servers of
  its IP version, if it has one; an answer comes back from the address the latest query from its port was sent to,
  while that is kept.
- With `ExcludeLocalNetwork`, the default, the capture of the routes' networks leaves out the local networks
  (`IpNetwork.LocalNetworks`); DNS queries to them are still captured with `SimulateDns`.
- `ProtectSocket` keeps a socket out of the capture by its TTL, `WinDivertVpnAdapter.ProtectedTtl`. There is no NAT
  and no app filter.

## Requirements

- x64 Windows, in an x64 process.
- An administrator's rights: the first start in a process installs WinDivert's DLL and driver into a protected folder
  of their own under Common Files, or takes the ones there, and the driver stays loaded until a reboot or until it is
  stopped.
- The package carries WinDivert's DLL and driver as its release ships them.

## License

LGPL-2.1-only. See the [VpnHood repository](https://github.com/vpnhood/VpnHood).

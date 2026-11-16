# VpnHood.Net.VpnAdapters.WindowsVpnPlugin

The VPN adapter of [VpnHood](https://github.com/vpnhood/VpnHood) for a Windows VPN plug-in: a packaged app's VPN,
which Windows lists in its VPN settings and runs over a `VpnChannel` of `Windows.Networking.Vpn`. It implements
`VpnHood.Net.VpnAdapters.Abstractions`.

```bash
dotnet add package VpnHood.Net.VpnAdapters.WindowsVpnPlugin
```

## How it works

- `WindowsVpnPluginAdapter` is an `IVpnAdapter`: make it in the plug-in's `Connect` with the `VpnChannel` Windows
  gave it, and use it as the
  [Abstractions quick start](https://github.com/vpnhood/VpnHood/blob/develop/src/Net/VpnHood.Net.VpnAdapters.Abstractions/README.md)
  uses `NullVpnAdapter`, with the VPN server's address in the options' `ServerIp`, which the channel's transport
  needs. Windows wants the channel started before `Connect` returns, so wait there for `StartedTask`, which completes
  once it has.
- Windows freezes the plug-in's process between VPN events; VpnHood's host keeps it running with a connection from
  its app to the package's app service.
- Windows owns the interface: a start gathers the addresses, routes, DNS servers and MTU, and the adapter starts the
  channel with them. Its MTU is at most 1,400 bytes. There is no NAT and no app filter.
- Pass the plug-in's `Encapsulate` callback to `OnEncapsulate`: the adapter reads the device's packets from there,
  and leaves their buffers to Windows. It writes incoming packets into the channel's receive buffers, so the
  plug-in's `Decapsulate` has nothing to do.
- A close leaves the channel up: stop it in the plug-in's `Disconnect`, as Windows takes a channel stopped by itself
  for a dropped connection and dials again.

## Requirements

- Windows 11 or later.
- A packaged app with the `networkingVpnProvider` capability and a `vpnClient` background task, whose `IVpnPlugIn`
  runs the adapter, and a VPN profile of the app for Windows to connect.

## License

LGPL-2.1-only. See the [VpnHood repository](https://github.com/vpnhood/VpnHood).

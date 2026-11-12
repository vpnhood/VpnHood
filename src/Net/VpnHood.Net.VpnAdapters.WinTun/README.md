# VpnHood.Net.VpnAdapters.WinTun

The Windows VPN adapter of [VpnHood](https://github.com/vpnhood/VpnHood), on [WinTun](https://www.wintun.net/): a
virtual network interface (TUN) that the host's traffic is routed into, whose IP packets a VPN reads and writes. It
implements `VpnHood.Net.VpnAdapters.Abstractions`.

```bash
dotnet add package VpnHood.Net.VpnAdapters.WinTun
```

## How it works

- `WinTunVpnAdapter` is an `IVpnAdapter`: make it with a `WinVpnAdapterSettings`, whose `AdapterName` names the
  interface, and use it as the
  [Abstractions quick start](https://github.com/vpnhood/VpnHood/blob/develop/src/Net/VpnHood.Net.VpnAdapters.Abstractions/README.md)
  uses `NullVpnAdapter`.
- A start makes the interface and sets its addresses, routes, MTU and metric through Windows' IP Helper API. Its DNS
  servers come with a Name Resolution Policy Table (NRPT) rule that makes them the only ones Windows asks, as
  Windows otherwise asks every adapter's servers at once; Group Policy's own NRPT rules override it, which the
  adapter logs.
- For a server, `UseNat` translates the virtual IPv4 network to the host's address through WinNAT; WinNAT has no
  IPv6. There is no app filter.
- `WinTunVpnAdapter.BuildGuidFromName` gives the interface's GUID for a name, the same at every start, so a kill
  switch can find the interface.

## Requirements

- Windows 10 or later, in an x64 or arm64 process.
- An administrator's rights: the first start in a process installs `wintun.dll` into a protected folder of its own
  under Common Files, and WinTun installs its driver with the first interface.
- The package carries WinTun's prebuilt DLLs, which come under WinTun's own Prebuilt Binaries License.

## License

LGPL-2.1-only. See the [VpnHood repository](https://github.com/vpnhood/VpnHood).

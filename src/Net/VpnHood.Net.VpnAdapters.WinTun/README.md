# VpnHood.Net.VpnAdapters.WinTun

The Windows VPN adapter of [VpnHood](https://github.com/vpnhood/VpnHood), on [WinTun](https://www.wintun.net/): a
virtual network interface (TUN) that the host's traffic is routed into, whose IP packets a VPN reads and writes. It
implements `VpnHood.Net.VpnAdapters.Abstractions`.

```bash
dotnet add package VpnHood.Net.VpnAdapters.WinTun
```

## Quick start

Open an adapter and handle the packets the host routes into it. The example compiles with the tests in CI
([VpnAdaptersReadmeExampleTest.cs](https://github.com/vpnhood/VpnHood/blob/develop/tests/VpnHood.Test/Tests/VpnAdaptersReadmeExampleTest.cs))
but does not run there, as it needs an administrator's rights. Its namespaces are `VpnHood.Net.VpnAdapters.WinTun`,
`VpnHood.Net.VpnAdapters.Abstractions` and `VpnHood.Net.Toolkit.Net`.

```csharp Snippet:VpnAdapterWinTun
// LinuxTunVpnAdapter, AndroidVpnAdapter and IosVpnAdapter take the same calls
using var adapter = new WinTunVpnAdapter(new WinVpnAdapterSettings {
    AdapterName = "MyVpn",
    AutoDisposePackets = true, // the adapter disposes each packet it has written to the host
    Blocking = false // a full queue drops its oldest packet instead of making a send wait
});

// every packet the host routes into the tunnel arrives here
adapter.PacketReceived += (_, ipPacket) => {
    Console.WriteLine($"{ipPacket.SourceAddress} -> {ipPacket.DestinationAddress}, {ipPacket.Protocol}");
    ipPacket.Dispose(); // the handler owns each packet: forward it or dispose it
};

await adapter.Start(new VpnAdapterOptions {
    SessionName = "My VPN",
    VirtualIpNetworkV4 = IpNetwork.Parse("10.255.0.2/24"),
    IncludeNetworks = IpNetwork.All
}, cancellationToken);
```

## How it works

- `WinTunVpnAdapter` is an `IVpnAdapter`: make it with a `WinVpnAdapterSettings`, whose `AdapterName` names the
  interface. The
  [Abstractions quick start](https://github.com/vpnhood/VpnHood/blob/develop/src/Net/VpnHood.Net.VpnAdapters.Abstractions/README.md)
  goes on from here: sending packets to the host, and stopping.
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

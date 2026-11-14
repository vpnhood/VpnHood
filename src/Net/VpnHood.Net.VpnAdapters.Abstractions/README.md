# VpnHood.Net.VpnAdapters.Abstractions

The contract and the base class of [VpnHood](https://github.com/vpnhood/VpnHood)'s VPN adapters: virtual network
interfaces (TUN) that the host's traffic is routed into, whose IP packets a VPN reads and writes. One package per
platform builds on it: `VpnHood.Net.VpnAdapters.WinTun`, `.WinDivert`, `.WindowsVpnPlugin`, `.LinuxTun`,
`.AndroidTun` and `.IosTun`.

```bash
dotnet add package VpnHood.Net.VpnAdapters.Abstractions
```

## How it works

- `IVpnAdapter` is the contract: start with `VpnAdapterOptions` (addresses, DNS servers, routes, NAT, app
  filter), read packets through `PacketReceived`, write them with `SendPacketQueued`, stop.
- `TunVpnAdapter` does the work every platform shares: the order of a start's steps, the stop, the reader and
  the writer with their retries, stopping after repeated I/O errors, `AutoRestart`, and following the host
  when its network changes. A platform's adapter supplies only its steps: add, open, close and remove the
  interface, set its addresses, routes and DNS servers, read and write one packet.
- `ProtectSocket` keeps a socket, such as the one to the VPN server, out of the tunnel.
- `NullVpnAdapter` has no interface: it runs a VPN without rights, for tests.

## Quick start

The example below runs as a test in CI
([VpnAdaptersReadmeExampleTest.cs](https://github.com/vpnhood/VpnHood/blob/develop/tests/VpnHood.Test/Tests/VpnAdaptersReadmeExampleTest.cs)).
Its namespaces are `System.Net`, `VpnHood.Net.VpnAdapters.Abstractions`, `VpnHood.Net.Packets`,
`VpnHood.Net.Packets.Extensions` and `VpnHood.Net.Toolkit.Net`.

```csharp Snippet:VpnAdapterStart
// in an app, a platform's adapter: WinTunVpnAdapter, LinuxTunVpnAdapter, AndroidVpnAdapter...
using var adapter = new NullVpnAdapter(blocking: false);

// what the host routes into the tunnel arrives here, on the adapter's reader task
adapter.PacketReceived += (_, ipPacket) => {
    Console.WriteLine($"From the host: {ipPacket}");
    ipPacket.Dispose(); // the handler owns each packet: forward it or dispose it
};

await adapter.Start(new VpnAdapterOptions {
    SessionName = "My VPN",
    VirtualIpNetworkV4 = IpNetwork.Parse("10.255.0.2/24"),
    DnsServers = [IPAddress.Parse("10.255.0.1")],
    IncludeNetworks = IpNetwork.All // every address of the IP versions the interface carries: IPv4 here
}, CancellationToken.None);

// what the VPN sends the host is queued; the adapter writes it on its own loop and disposes it
var packet = PacketBuilder.BuildUdp(
    new IPEndPoint(IPAddress.Parse("10.255.0.1"), 53),
    new IPEndPoint(IPAddress.Parse("10.255.0.2"), 5353),
    "answer"u8);
packet.UpdateAllChecksums();
adapter.SendPacketQueued(packet);

adapter.Stop(); // removes the interface, its routes and its DNS servers
```

## License

LGPL-2.1-only. See the [VpnHood repository](https://github.com/vpnhood/VpnHood).

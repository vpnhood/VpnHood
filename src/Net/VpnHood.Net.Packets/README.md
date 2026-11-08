# VpnHood.Net.Packets

A fast, low-allocation IP packet library for .NET: parse and build IPv4, IPv6, TCP, UDP, ICMPv4 and ICMPv6
packets in place, and compute their Internet checksums with SIMD. It is the packet layer of
[VpnHood](https://github.com/vpnhood/VpnHood), and works on its own for any .NET app that reads or writes raw
packets: a TUN device, a raw socket, a capture file.

```bash
dotnet add package VpnHood.Net.Packets
```

## How it works

- A packet is a view over its bytes: `IpV4Packet`, `IpV6Packet`, `TcpPacket`, `UdpPacket` and the ICMP packets
  read and write the buffer in place, with no copy.
- `PacketBuilder` parses packets and builds new ones in memory rented from a pool. Dispose a packet to return
  its memory.
- `BuildIp`, `BuildUdp`, `BuildTcp`, `BuildIcmpV4`, `BuildIcmpV6` and `BuildDns` compute no checksum: call
  `UpdateAllChecksums()` before sending. The echo, reset and ICMP error builders compute them unless told not to.
- An IPv6 packet reads through its Hop-by-Hop, Destination Options, Fragment and Authentication headers to the
  upper layer.

## Quick start

Each example below runs as a test in CI
([PacketsReadmeExampleTest.cs](https://github.com/vpnhood/VpnHood/blob/develop/tests/VpnHood.Test/Tests/PacketsReadmeExampleTest.cs)).
Their namespaces are `System.Net`, `VpnHood.Net.Packets`, `VpnHood.Net.Packets.Extensions` and
`VpnHood.Net.Toolkit.Net`.

### Build a UDP packet

```csharp Snippet:PacketsBuildUdp
using var ipPacket = PacketBuilder.BuildUdp(
    new IPEndPoint(IPAddress.Parse("10.0.0.2"), 5353),
    new IPEndPoint(IPAddress.Parse("10.0.0.1"), 53),
    "hello"u8);

ipPacket.UpdateAllChecksums(); // BuildUdp computes no checksum itself
var bytes = ipPacket.Buffer; // the packet's own bytes, ready for a TUN device
```

### Parse a packet

```csharp Snippet:PacketsParse
// Parse copies the received bytes into pooled memory; PacketBuilder.Attach would read them in place
using var ipPacket = PacketBuilder.Parse(received);
Console.WriteLine($"{ipPacket.SourceAddress} -> {ipPacket.DestinationAddress}, {ipPacket.Protocol}");

if (ipPacket.Protocol == IpProtocol.Udp && !ipPacket.IsFragment) {
    var udp = ipPacket.ExtractUdp(); // a view over the packet's bytes, not a copy
    Console.WriteLine($"UDP {udp.SourcePort} -> {udp.DestinationPort}, {udp.Payload.Length} bytes, " +
                      $"checksum valid: {udp.IsChecksumValid(ipPacket)}");
}
```

### Answer a ping

```csharp Snippet:PacketsPing
using var request = PacketBuilder.BuildIcmpEchoRequest(
    IPAddress.Parse("10.0.0.2"), IPAddress.Parse("10.0.0.1"), "ping"u8,
    identifier: 1, sequenceNumber: 1); // checksums computed

// answer it as a host would: swap the addresses, keep the identifier, sequence number and data
var echo = request.ExtractIcmpV4();
using var reply = PacketBuilder.BuildIcmpEchoReply(
    request.DestinationAddress, request.SourceAddress, echo.Payload.Span,
    echo.Identifier, echo.SequenceNumber);
```

## License

LGPL-2.1-only. See the [VpnHood repository](https://github.com/vpnhood/VpnHood).

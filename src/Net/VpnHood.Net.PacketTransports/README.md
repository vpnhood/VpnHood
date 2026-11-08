# VpnHood.Net.PacketTransports

Packet queues for .NET: a transport takes IP packets from many senders, queues them, and hands them to your code
on a sending loop of its own, while the packets your code reads go up through one event. It is how
[VpnHood](https://github.com/vpnhood/VpnHood) moves packets between its tunnels, its adapters and its TCP stack,
and works for any channel of `VpnHood.Net.Packets` packets: a device, a socket, a pipe.

```bash
dotnet add package VpnHood.Net.PacketTransports
```

## How it works

- `IPacketTransport` is the two-way contract: `SendPacketQueued` and `SendPacketQueuedAsync` queue a packet to
  send, and `PacketReceived` raises each packet received.
- Derive from the base that fits how your channel writes:
  - `PacketTransport` hands over batches, up to the queue's capacity at a time;
  - `SinglePacketTransport` hands over one packet at a time;
  - `PassthroughPacketTransport` has no queue: each packet goes on at once, on the sender's thread.
- `PacketTransportOptions` sets the queue: its capacity, whether a send to a full queue waits for room
  (`Blocking`, up to `BlockingTimeout`) or drops the oldest packet, and whether the transport disposes each
  packet once sent (`AutoDisposePackets`).
- A packet has one owner at a time. One sent to the transport is the transport's, whatever the outcome; one
  raised by `PacketReceived` is the handler's, which forwards or disposes it, so attach one handler only.
- `PacketStat` counts what is sent, received and dropped.
- `FairPacketQueue` is a send queue split into lanes by a hash of a packet's ports, so one flow that floods
  fills mostly its own lane.

## Quick start

The examples below run as a test in CI
([PacketTransportsReadmeExampleTest.cs](https://github.com/vpnhood/VpnHood/blob/develop/tests/VpnHood.Test/Tests/PacketTransportsReadmeExampleTest.cs)).
Their namespaces are `System.Net`, `VpnHood.Net.Packets`, `VpnHood.Net.Packets.Extensions` and
`VpnHood.Net.PacketTransports`.

### A transport over your device

```csharp Snippet:PacketTransportDevice
// A transport over your own device: the queue, its sending loop and the packet ownership come from the base.
public sealed class DeviceTransport() : PacketTransport(new PacketTransportOptions {
    AutoDisposePackets = true, // the base disposes each packet once SendPacketsAsync returns
    Blocking = false // a full queue drops its oldest packet instead of waiting for room
})
{
    public List<string> Written { get; } = [];

    // the sending loop hands over a batch at a time
    protected override ValueTask SendPacketsAsync(IReadOnlyList<IpPacket> ipPackets)
    {
        foreach (var ipPacket in ipPackets)
            Written.Add(ipPacket.ToString()); // write it to the device here
        return default;
    }

    // what the device reads goes up through PacketReceived
    public void OnDeviceRead(IpPacket ipPacket) => OnPacketReceived(ipPacket);
}
```

### Send and receive

```csharp Snippet:PacketTransportUse
using var transport = new DeviceTransport();

// one handler, which owns each received packet: it forwards or disposes it
transport.PacketReceived += (_, ipPacket) => {
    Console.WriteLine($"Received: {ipPacket}");
    ipPacket.Dispose();
};

// a packet sent is the transport's from here, whatever the outcome
var packet = PacketBuilder.BuildUdp(
    new IPEndPoint(IPAddress.Parse("10.0.0.2"), 5353),
    new IPEndPoint(IPAddress.Parse("10.0.0.1"), 53),
    "query"u8);
packet.UpdateAllChecksums();
transport.SendPacketQueued(packet);
```

## Packages

- `VpnHood.Net.PacketTransports` — the transports
- `VpnHood.Net.Packets` — the packets they carry

Requires .NET 11.

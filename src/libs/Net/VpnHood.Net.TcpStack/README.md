# VpnHood.Net.TcpStack

A lightweight user-space TCP stack for .NET. Feed it raw IP packets — from a TUN device, WinDivert,
or anything else that hands you packets — and it terminates the TCP connections inside them and gives
you each one as an ordinary .NET `Stream`, the way `TcpListener` gives a socket's connections.

It is built for local links, where packets arrive in order and are rarely lost: a virtual adapter on
the same machine. That is what keeps it small and fast.

## Features

- **Stream API**: every accepted connection is a `LocalTcpClient` with a standard `Stream`
- **IPv4 and IPv6**: listeners and connections work the same with either family
- **Listen on one endpoint or on all**: `Listen(endPoint)` for one address and port exactly, or `ListenAny()`
  for every connection no endpoint listener takes, as a transparent proxy needs
- **Sized per platform**: `LocalTcpStackOptions` sets windows, buffers and connection limits; the
  `Ios` preset is a small-footprint starting point for an iOS Network Extension
- **Loss recovery**: a retransmission timer, fast retransmit and recovery on partial ACKs, so a lost
  segment does not stall a connection
- **Pure library**: no native code; it builds on `VpnHood.Net.Packets` for parsing and building packets

## Quick start

Any `IVpnAdapter` from `VpnHood.Net.VpnAdapters.*` (WinDivert, WinTun, Linux TUN, Android, iOS) is a
packet source. Route a virtual endpoint through the adapter and the stack answers it:

```csharp
using var tcpStack = new LocalTcpStack();

// Packets the stack produces go out through your adapter, which owns each one from here: made with
// AutoDisposePackets = true, it disposes each packet once written.
tcpStack.OnPacketSend = packet => adapter.SendPacketQueued(packet);

// Packets your adapter captures go into the stack, which copies what it keeps: the handler still owns each one.
adapter.PacketReceived += (_, packet) => {
    tcpStack.ProcessIncoming(packet);
    packet.Dispose();
};

// Listen before the adapter starts: a SYN for an endpoint with no listener draws a reset.
using var listener = tcpStack.Listen(new IpEndPointValue(IPAddress.Parse("11.0.0.1"), 8080));

// Start the adapter before the accept loop, which waits for connections.
await adapter.Start(new VpnAdapterOptions {
    SessionName = "demo",
    VirtualIpNetworkV4 = IpNetwork.Parse("10.0.0.0/24"),
    IncludeNetworks = [new IpNetwork(IPAddress.Parse("11.0.0.1"), 32)]
}, cancellationToken);

// Accept connections to the virtual endpoint.
await foreach (var client in listener.AcceptAllAsync(cancellationToken))
    _ = HandleAsync(client);
```

## Example: echo server

```csharp
static async Task HandleAsync(LocalTcpClient client)
{
    await using (client) {
        var buffer = new byte[4096];
        int read;
        while ((read = await client.Stream.ReadAsync(buffer)) > 0)
            await client.Stream.WriteAsync(buffer.AsMemory(0, read));
    }
}
```

## How it fits together

```text
adapter (TUN / WinDivert)
    │  PacketReceived
    ▼
LocalTcpStack.ProcessIncoming()
    │  completed handshake ──▶ LocalTcpListener.AcceptAllAsync() ──▶ LocalTcpClient
    │  data  ──▶ LocalTcpConnection ──▶ LocalTcpStream (read)
    │
    │  LocalTcpStream (write)
    ▼
LocalTcpStack.OnPacketSend
    │
    ▼
adapter.SendPacketQueued()
```

## Limits

- **No congestion control.** A local link has no congestion to control. The retransmission timer backs off,
  but the send rate does not: on a congested network path the stack would not slow down.
- **No receive window scaling.** Our window is at most 64 KB per connection; the peer's scale is honored.
- **No out-of-order reassembly.** A segment ahead of the next expected byte is dropped and answered with a
  duplicate ACK; the peer retransmits it. No SACK.
- **No TIME_WAIT.** A closed connection's 4-tuple is free at once. A SYN on a live tuple draws a challenge ACK,
  or the SYN-ACK again while the handshake is open. A FIN the peer retransmits because our last ACK was lost
  draws a RST; the peer had acknowledged our FIN and every byte before it.
- **A simplified state machine.** It covers what reliable local connections need, not every corner
  of RFC 9293.

## Packages

- `VpnHood.Net.TcpStack` — the stack
- `VpnHood.Net.TcpStack.Abstractions` — `ITcpStack`, `ITcpListener`, `ITcpClient`, for code that takes
  a stack without depending on this one

Requires .NET 11.

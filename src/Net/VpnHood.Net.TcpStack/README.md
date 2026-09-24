# VpnHood.Net.TcpStack

A lightweight user-space TCP stack for .NET. Feed it raw IP packets — from a TUN device, WinDivert,
or anything else that hands you packets — and it terminates the TCP connections inside them and gives
you each one as an ordinary .NET `Stream`, much like `TcpListener` does for real sockets.

It is built for local links, where packets arrive in order and are rarely lost: a virtual adapter on
the same machine. That is what keeps it small and fast.

## Features

- **Stream API**: every accepted connection is a `LocalTcpClient` with a standard `Stream`
- **IPv4 and IPv6**: listeners and connections work the same with either family
- **Listen on one endpoint or on all**: `Listen(endPoint)` like `TcpListener`, or `ListenAny()` to take
  every connection, as a transparent proxy does
- **Sized per platform**: `LocalTcpStackOptions` sets windows, buffers and connection limits; the
  `Ios` preset fits an iOS Network Extension's memory limit
- **Loss recovery**: a retransmission timer and delayed ACKs, so a lost segment does not stall a
  connection
- **Pure library**: no native code; it builds on `VpnHood.Net.Packets` for parsing and building packets

## Quick start

```csharp
using var tcpStack = new LocalTcpStack();

// Packets the stack produces go back out through your adapter. The consumer owns each packet.
tcpStack.OnPacketSend = packet => adapter.SendPacketQueued(packet);

// Packets your adapter captures go into the stack.
adapter.PacketReceived += (_, packet) => tcpStack.ProcessIncoming(packet);

// Accept connections to a virtual endpoint.
using var listener = tcpStack.Listen(new IpEndPointValue(IPAddress.Parse("11.0.0.1"), 8080));
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

## With a VpnHood adapter

Any `IVpnAdapter` from `VpnHood.Net.VpnAdapters.*` (WinDivert, WinTun, Linux TUN, Android, iOS) is a
packet source. Route the virtual endpoint through the adapter and the stack answers it:

```csharp
await adapter.Start(new VpnAdapterOptions {
    SessionName = "demo",
    VirtualIpNetworkV4 = IpNetwork.Parse("10.0.0.0/24"),
    IncludeNetworks = [new IpNetwork(IPAddress.Parse("11.0.0.1"), 32)]
}, cancellationToken);
```

## How it fits together

```text
adapter (TUN / WinDivert)
    │  PacketReceived
    ▼
LocalTcpStack.ProcessIncoming()
    │  SYN   ──▶ LocalTcpListener.AcceptAllAsync() ──▶ LocalTcpClient
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

- **No congestion control.** A local link has no congestion to control; on a lossy network path the
  stack would not back off.
- **No window scaling.** The receive window is at most 64 KB per connection.
- **A simplified state machine.** It covers what reliable local connections need, not every corner
  of RFC 9293.

## Packages

- `VpnHood.Net.TcpStack` — the stack
- `VpnHood.Net.TcpStack.Abstractions` — `ITcpStack`, `ITcpListener`, `ITcpClient`, for code that takes
  a stack without depending on this one

Requires .NET 10.

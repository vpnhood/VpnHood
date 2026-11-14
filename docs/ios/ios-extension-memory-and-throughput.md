# VpnHood iOS Network Extension: Memory and Throughput

The packet tunnel extension on iOS and tvOS runs under a memory limit of about 50 MB. Past it, the system
kills the extension (jetsam) and the VPN drops. This page maps what spends that budget and which code bounds
it; the comments in that code hold the values and the measurements behind them. Read it before changing
memory, throughput or TCP-stack code.

## What the limit counts

- Jetsam counts `phys_footprint`: dirty plus compressed anonymous memory. File-backed code pages don't count,
  so trimming, linking or AOT doesn't move it.
- The runtime is most of the idle floor, not our code. CoreCLR (.NET 11) idles at about 23 MB where Mono idled
  at about 42 MB, and its generational GC absorbs the per-packet allocation churn that Mono spiked on.
- The managed heap is a small part of the footprint, but it can jump by megabytes in a burst of downloaded
  packets. `IosMemoryGuard` forces collections and drains finalizers to keep it down; its comment says why it
  must stay. The GC settings are CoreCLR's (`System.GC.ConserveMemory` in the extension's csproj): Mono's
  `MONO_GC_PARAMS` has no effect.
- `NSExtensionPrincipalClass` names the extension's own `PacketTunnelProvider`, a subclass of `IosVpnService`.
  Naming the core class crashes the extension on launch: CoreCLR's registrar cannot find the core assembly.

## TCP forwarding

Memory grows with concurrent flows. A full tunnel carries every app's connections, not only the page on
screen: a speed test opened 116 at once and browsing 192, most of them idle keep-alives.
`LocalTcpStackOptions.Ios` bounds them:

- At most 40 connections. At the cap, a new flow evicts the most idle one (idle 15 s or more), and idle flows
  are reaped after 20 s.
- A 64 KB receive window per flow, without window scaling, so 40 flows hold at most 2.5 MB unread.

Throughput comes from the window re-opening, not from its size. A sender that saw a zero window sends nothing
until it learns the window opened again; `ReceiveWindow` sends that update whenever the window slides open by
a quarter. Without it, upload stalls at about 2 Mbps, and larger windows or buffers don't help.

## Memory gates

The extension idles at about 30 MB, and a burst of traffic allocates about 10 MB of native buffers within
moments. The gates act in turn as the footprint rises:

| Footprint | Code | What it does |
| --- | --- | --- |
| 41 MB | `ConnectorService` | Holds new connections to the server |
| 42 MB | `LocalTcpStackOptions.AdmissionMemoryLimitMb` | Drops new SYNs silently, so the apps' own SYN retries pace the flows; the QUIC download ingest gate closes at the same footprint |
| 44 MB | `QuicStreamConnectionFactory` | A 250 ms watchdog recycles every QUIC connection, which frees their native buffers; flows reconnect |

Gates set close to the idle footprint trip on ordinary bursts and cut the connection pool. The top gate leaves
room below the limit for native allocations that land between two readings.

## Transport buffers

iOS and tvOS take `ClientTransportOptions.LimitedMemory`:

- Two packet channels, small packet-channel and UDP buffers, and 32 KB stream-forwarding buffers.
- 64 KB kernel buffers for each forwarded or excluded socket, as up to 40 of them pay it.
- The packet channels' kernel buffers are left to the OS. Setting `SO_SNDBUF` or `SO_RCVBUF` turns Darwin's
  autotuning off: a fixed 256 KB capped download at about 26 Mbps at 75 ms RTT, where autotuning reached
  210 Mbps down and about 100 Mbps up for about 1 MB more footprint.

An iPad app running on an Apple-silicon Mac has no extension memory limit, so its `AppDelegate` takes
`NormalMemory` there.

## Packet writes

`IosVpnAdapter.SendPacketsAsync` hands iOS up to 32 packets per `WritePackets` call, from reused arrays, in one
autorelease pool per drain. A final, shorter chunk gets an array of its exact length, as `WritePackets` walks
the whole array and aborts on a trailing null. The protocol-family numbers are cached: a new `NSNumber` per
packet leaks native memory faster than finalization frees it.

## Known limit: downloads share one packet channel

`ChannelManager` picks a packet channel by `SourcePort % channelCount`. Download packets carry the service's
port, 443 for nearly everything, so all downloads share one channel while uploads spread over all of them.
Hashing both ports would need a server release. With autotuning, one channel carried 210 Mbps.

## Diagnostics

Logging below Information (the `/log:debug` debug command) turns on the extension's diagnostics: `vpn-ext.log`
gets the TCP stack's and QUIC's connection lines, and `Documents/ext-mem.log` the footprint breakdown that
`IosMemoryMonitor` describes.

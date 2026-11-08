# VpnHood.Net.Quic.Ios

iOS QUIC client backed by Apple's **Network.framework** (`NWConnectionGroup` /
`NWMultiplexGroup`, iOS 15+). It implements the same `VpnHood.Net.Quic.Abstractions` surface
(`IQuicClient` / `IQuicConnection` / stream) as the desktop **MsQuic** client, so the tunneling layer
consumes QUIC identically on every platform. There is **no MsQuic on iOS** — `libmsquic` is not
shippable inside a Network Extension, so this project bridges to the OS's own QUIC stack instead.

> This document is the authoritative record of the iOS QUIC implementation. It lives with the code
> (not in the iOS app repo) because everything here is QUIC/Network.framework-specific and platform
> code in `../VpnHood.Net.Quic.Ios` is referenced by `ProjectReference`, never copied.

## Why this exists (memory, not features)

The driving constraint is the iOS Network Extension **~52 MB jetsam limit**. QUIC receive buffering
on iOS lands in **native** memory (Network.framework), which `phys_footprint` counts and jetsam
enforces — so the whole design is about **bounding native receive buffering** while keeping
throughput. See the shared record in the app repo's
`docs/ios/ios-extension-memory-and-throughput.md` for the broader memory story.

## Files

| File | Role |
|---|---|
| `IosQuicClient.cs` | `IQuicClient`. `ConnectAsync` stands up the QUIC tunnel: builds `NWParameters.CreateQuic` (ALPN `h3`, **flow-control windows**, pinned-cert TLS bridge), starts an `NWConnectionGroup` over an `NWMultiplexGroup`, waits for `Ready`. |
| `IosQuicConnection.cs` | `IQuicConnection`. Opens outbound streams (`nw_connection_group_extract_connection` with a NULL endpoint) and accepts inbound (peer-initiated) streams from a channel; brings each up on a queue until `Ready`. |
| `IosQuicStream.cs` | `Stream` adapter over a single QUIC stream (`NWConnection`). One armed native receive per `ReadAsync`, no read-ahead; per-operation callback state prevents late native completions from completing a later operation. |
| `IosQuicStreamReadOperation.cs`, `IosQuicStreamWriteOperation.cs` | That per-operation state: one read's or one write's value-task source, which its native completion reserves before it touches the caller's buffer. |
| `ReusableValueTaskSource.cs`, `ReusableValueTaskSource_1.cs` | `IValueTaskSource[<T>]` bridge used by read/write operation state to complete Network.framework callbacks as `ValueTask`s. |
| `IosQuicTls.cs` | Configures `SecProtocolOptions`: ALPN + the pinned-certificate verify callback bridge. |
| `NWConnectionExtensions.cs`, `NWConnectionGroupExtensions.cs` | Best-effort teardown of a stream and of the group: each call is attempted and logged, never masking the failure that led to it. |
| `IosQuicDiagnostics.cs`, `IosQuicEventIds.cs` | The stream counters and `[VHQUIC]` log lines (see Diagnostics), and their log event id. |
| `IosSocketFactory.cs` | Small factory glue. |

## Connection & stream model

- **One QUIC tunnel = `NWMultiplexGroup` + `NWConnectionGroup`.** Streams are individual
  `NWConnection`s multiplexed over that group.
- **Outbound stream:** `nw_connection_group_extract_connection(group, NULL, NULL)` — the native
  equivalent of Swift's `NWConnection(from: group)`. The managed `ExtractConnection` binding NREs on
  a null endpoint, and passing the tunnel endpoint / fresh QUIC options makes the extracted stream
  try to stand up its **own** transport and fail (`ENETDOWN`). So we P/Invoke the native function
  directly with **NULL endpoint AND NULL options** so the stream inherits the group's tunnel + QUIC
  options. (See the `[DllImport]` in `IosQuicConnection`.)
- **Inbound streams:** the group's new-connection handler (wired **before** `Start()` so none are
  missed) writes them to an unbounded channel; `AcceptInboundStreamAsync` drains it.
- With TCP forwarding there is **one QUIC stream per forwarded flow** — this is why stream lifecycle and the
  per-stream window dominate memory (see below).

## Flow control (the load-bearing numbers)

`IosSocketFactory.CreateQuicClient` picks them, times its memory scale (at least 1), and
`IosQuicClient.ConnectAsync` sets them on the QUIC options block; an `IosQuicClient` made directly
keeps Network.framework's defaults:

| Parameter | Value | Why |
|---|---|---|
| `InitialMaxData` | **16 KB** | Connection-wide initial flow-control credit across all streams. Tuned down in steps after measured sub-second native transient spikes near the jetsam limit; prioritizes extension stability over peak iOS download speed. |
| `InitialMaxStreamDataBidirectionalLocal` | **4 KB** | Per-stream credit for streams we open (TCP forwarding's download streams). |
| `InitialMaxStreamDataBidirectionalRemote` | **4 KB** | Per-stream credit for peer-opened streams. |
| `IosQuicStream.MaxReceiveLength` | **16 KB** | Caps a single armed native receive so even a huge caller buffer pulls native data in bounded chunks. |

Without an explicit window, Network.framework advertises a large default and a download burst floods
native receive buffers faster than forwarding drains them — measured **+16 MB in ~1 s at ~25
streams** → jetsam kill. These are the initial credits the client advertises. Network.framework grants more as it buffers
data, not as we read it, so they bound no native memory, and the read path has no memory brake of
its own.

### ⚠️ Interaction with the shared `LocalTcpStack` (read this before tuning windows)

QUIC's **tight** windows make this transport *less* forgiving than kernel TCP, which means it
**exposes send-side stalls in the shared TCP stack that big OS socket buffers otherwise hide.** The
QUIC+TcpForwarding download collapse (40→3 Mbps) was exactly this: a `LocalTcpConnection` Zero Window
Probe that failed to make forward progress stalled the forwarding pump; kernel TCP masked it, QUIC did
not. That bug and its forward-progress invariant live in
`../VpnHood.Net.TcpStack/LocalTcpConnection.cs` (`SendZeroWindowProbe`) with a regression test in
`tests/VpnHood.Net.TcpStack.Test`. **If you shrink these windows, re-verify the TcpStack send path
under sustained download.**

## Threading

Network.framework delivers a connection's callbacks (receive, FIN/close, state, and `Cancel`
teardown) serially on its assigned queue. The QUIC tunnel and extracted streams currently share the
same `VpnHood.Quic.Ios` queue. Stream callbacks are therefore serialized through one queue, with no per-stream queue pool.

## Native interop gotchas

- **QUIC options re-wrap:** the `NWParameters.CreateQuic` callback argument is typed as the base
  `NWProtocolOptions` even though the native object is a QUIC options block. A direct C# cast to
  `NWProtocolQuicOptions` throws `InvalidCastException`, and thrown on the native trampoline thread it
  **SIGABRTs the process**. Re-wrap the **same** native handle via
  `Runtime.GetINativeObject<NWProtocolQuicOptions>(handle, owns:false)` before setting options.
- **NULL-endpoint stream extraction:** see "Outbound stream" above — direct `[DllImport]` of
  `nw_connection_group_extract_connection`, returns a +1-retained `nw_connection_t` (`owns:true`).
- **`NSAutoreleasePool`** wraps native receive/send arming and callbacks to release autoreleased
  temporaries promptly (matters under burst).

## Read / write semantics

- **Read:** one native `ReceiveData(minimumIncompleteLength: 1, maximumLength: min(buf, 16 KB))` per
  `ReadAsync`; the completion **reserves** the value-task source *before* touching the caller buffer
  (`TryReserve()` on the read operation's `Source`) — if cancellation/Dispose already completed the
  read, the buffer may have been returned to a shared pool and re-rented, so copying into it would
  corrupt unrelated memory. Empty completion ⇒ EOF (returns 0); a non-empty final chunk is returned,
  the EOF is kept, and the next read returns 0 without a native receive.
- **Write:** `isComplete:false` always (never signals FIN mid-stream). The bytes are copied into a
  `DispatchData`, native-owned storage that Network.framework keeps for the asynchronous send.
- **Dispose:** `Cancel()` + `Dispose()` the `NWConnection`, dispose pending cancellation
  registrations (native callbacks may never fire after `Cancel()`), then unblock any pending
  read/write. Second native completions are no-ops via the value-task source's state guard.

## Diagnostics

`IosQuicDiagnostics` holds the stream counters and the `[VHQUIC] +CONN/-CONN live=… id=…` log lines,
which confirm streams (= native `NWConnection`s) are released promptly at flow-end rather than
lingering. Its counters move only while it is enabled (a log level below Information), so production pays
nothing; `TakeMaxStreamCancelMs` resets its figure either way. `LiveStreamCount`
is `public static` so the iOS memory probe in another assembly can read it.

## Targeting

`net*-ios` (Apple platforms only); referenced by the iOS device/extension via `ProjectReference`.
Requires iOS 15+ (`IosQuicClient.IsSupported`).
</content>
</invoke>

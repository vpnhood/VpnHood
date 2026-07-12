# VpnHood.Core.Quic.Ios

iOS/tvOS QUIC client backed by Apple's **Network.framework** (`NWConnectionGroup` /
`NWMultiplexGroup`, iOS 15+). It implements the same `VpnHood.Core.Quic.Abstractions` surface
(`IQuicClient` / `IQuicConnection` / stream) as the desktop **MsQuic** client, so the tunneling layer
consumes QUIC identically on every platform. There is **no MsQuic on iOS** — `libmsquic` is not
shippable inside a Network Extension, so this project bridges to the OS's own QUIC stack instead.

> This document is the authoritative record of the iOS QUIC implementation. It lives with the code
> (not in the iOS app repo) because everything here is QUIC/Network.framework-specific and platform
> code in `../VpnHood.Core.Quic.Ios` is referenced by `ProjectReference`, never copied.

## Why this exists (memory, not features)

The driving constraint is the iOS Network Extension **~52 MB jetsam limit**. QUIC receive buffering
on iOS lands in **native** memory (Network.framework), which `phys_footprint` counts and jetsam
enforces — so the whole design is about **bounding native receive buffering** while keeping
throughput. See the shared record in the app repo's
`docs/ios-extension-memory-and-throughput.md` for the broader memory story.

## Files

| File | Role |
|---|---|
| `IosQuicClient.cs` | `IQuicClient`. `ConnectAsync` stands up the QUIC tunnel: builds `NWParameters.CreateQuic` (ALPN `h3`, **flow-control windows**, pinned-cert TLS bridge), starts an `NWConnectionGroup` over an `NWMultiplexGroup`, waits for `Ready`. |
| `IosQuicConnection.cs` | `IQuicConnection`. Opens outbound streams (`nw_connection_group_extract_connection` with a NULL endpoint) and accepts inbound (peer-initiated) streams from a channel; brings each up on a queue until `Ready`. |
| `IosQuicStream.cs` | `Stream` adapter over a single QUIC stream (`NWConnection`). One armed native receive per `ReadAsync`, no read-ahead; per-operation callback state prevents late native completions from completing a later operation. |
| `ReusableValueTaskSource.cs` | `IValueTaskSource[<T>]` bridge used by read/write operation state to complete Network.framework callbacks as `ValueTask`s. |
| `IosQuicTls.cs` | Configures `SecProtocolOptions`: ALPN + the pinned-certificate verify callback bridge. |
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
- In proxy mode there is **one QUIC stream per proxied flow** — this is why stream lifecycle and the
  per-stream window dominate memory (see below).

## Flow control & native-memory cap (the load-bearing numbers)

Set in `IosQuicClient.ConnectAsync` on the QUIC options block:

| Parameter | Value | Why |
|---|---|---|
| `InitialMaxData` | **256 KB** | Connection-wide aggregate receive window across all streams — the real ceiling on native inbound buffering. Tuned down from 1 MB after measured sub-second native transient spikes near the jetsam limit; prioritizes extension stability over peak iOS download speed. |
| `InitialMaxStreamDataBidirectionalLocal` | **32 KB** | Per-stream window for streams we open (the proxy's download streams). |
| `InitialMaxStreamDataBidirectionalRemote` | **32 KB** | Per-stream window for peer-opened streams. |
| `IosQuicStream.MaxReceiveLength` | **16 KB** | Caps a single armed native receive so even a huge caller buffer pulls native data in bounded chunks. The QUIC window above is the real ceiling; this just bounds one receive. |

Without an explicit window, Network.framework advertises a large default and a download burst floods
native receive buffers faster than the proxy drains them — measured **+16 MB in ~1 s at ~25
streams** → jetsam kill. These windows are real QUIC flow control: the server may not send past the
window until we consume and the stack emits window updates.

### ⚠️ Interaction with the shared `LocalTcpStack` (read this before tuning windows)

QUIC's **tight** windows make this transport *less* forgiving than kernel TCP, which means it
**exposes send-side stalls in the shared TCP stack that big OS socket buffers otherwise hide.** The
QUIC+TcpProxy download collapse (40→3 Mbps) was exactly this: a `LocalTcpConnection` Zero Window
Probe that failed to make forward progress stalled the proxy pump; kernel TCP masked it, QUIC did
not. That bug and its forward-progress invariant live in
`../VpnHood.Core.TcpStack/LocalTcpConnection.cs` (`SendZeroWindowProbe`) with a regression test in
`tests/VpnHood.Core.TcpStack.Test`. **If you shrink these windows, re-verify the TcpStack send path
under sustained download.**

## Threading

Network.framework delivers a connection's callbacks (receive, FIN/close, state, and `Cancel`
teardown) serially on its assigned queue. The QUIC tunnel and extracted streams currently share the
same `VpnHood.Quic.Ios` queue. Stream callbacks are therefore serialized through one queue; the
native-memory cap is handled by QUIC flow-control windows and bounded receives rather than a
per-stream queue pool.

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

- **Read:** one native `ReceiveReadOnlyData(min:1, max:min(buf, 16 KB))` per `ReadAsync`; the
  completion **reserves** the value-task source *before* touching the caller buffer
  (`_readSource.TryReserve()`) — if cancellation/Dispose already completed the read, the buffer may
  have been returned to a shared pool and re-rented, so copying into it would corrupt unrelated
  memory. Empty completion ⇒ EOF (returns 0); a non-empty final chunk is returned and the next read
  observes empty → 0.
- **Write:** `isComplete:false` always (never signals FIN mid-stream). Non-array-backed buffers are
  copied through a pooled rental returned in the write completion (idempotent via
  `Interlocked.Exchange`).
- **Dispose:** `Cancel()` + `Dispose()` the `NWConnection`, dispose pending cancellation
  registrations (native callbacks may never fire after `Cancel()`), then unblock any pending
  read/write and reclaim an in-flight write's rental. Second native completions are no-ops via the
  value-task source's state guard.

## Diagnostics (temporary — marked `ToDo: remove diagnose`)

`IosQuicClient.LiveStreamCount` / `StreamSeq` and the `[VHQUIC] +CONN/-CONN live=… id=…` log lines
in `IosQuicStream` exist to confirm streams (= native `NWConnection`s) are released promptly at
flow-end rather than lingering. `LiveStreamCount` is `public static` so the iOS memory probe in
another assembly can read it. Remove once the lifecycle is settled.

## Targeting

`net*-ios` (Apple platforms only); referenced by the iOS device/extension via `ProjectReference`.
Requires iOS 15+ (`IosQuicClient.IsSupported`).
</content>
</invoke>

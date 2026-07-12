# iOS Network Extension — Memory & Throughput (consolidated)

**Status:** proxy mode working on iOS with good throughput + bounded memory (on .NET 11 preview).
**Investigation:** 2026-06-16 → 2026-06-19 • **Device:** iPhone 11 (A13, 16 KB pages, iOS 26.5),
hardware UDID `00008030-001544500A38802E` • **Builds with:** `~/.dotnet11/dotnet`
(SDK 11.0.100-preview.5, iOS workload `26.5.11546-net11-p5`).

> This file merges the three working docs (`ios-memory-jetsam-investigation`, `ios-net11-coreclr-proxy-speed`,
> and Gemini's `upload-speed-and-memory-stabilization`) into one accurate record. Code changes live in the
> core projects under `src/Core`; the app glue is under `src/Apps/{Client,Connect}.Ios[.Extension]`. The
> **batched native tun write** (Part 4) lives on
> core branch `feat/ios-tun-batch-write`; the rest landed on `development`.

---

## Goals
1. Stop the **~52 MB jetsam kill** of the Network Extension. ✅ (.NET 11 / CoreCLR)
2. Keep **`UseTcpProxy=true` viable on iOS** — split-domain tunneling *requires* it
   (`AppFeatures.IsSplitDomainSupported = device.IsTcpProxySupported`). No packet-mode fallback. ✅
3. **Restore throughput** (upload + download). ✅ (dynamic-window re-open)
4. Fix the **shared TCP stack** so Android/desktop also bound memory, not just iOS.

---

## Executive summary (what's true now)
- The old ~42 MB floor was the **Mono runtime**, not our code. **.NET 11 / CoreCLR halves it to ~23 MB and
  stays flat under load** → the jetsam crash is gone. (Required re-adding the `PacketTunnelProvider` subclass —
  CoreCLR's registrar otherwise can't find the principal class's assembly.)
- Proxy mode had **two memory failure modes**, both fixed: (A) **connection pile-up** — full-tunnel browsing
  opens ~190 concurrent flows × ~180 KB; (B) **upload-burst pipe balloon** — a fixed window never throttled the
  sender. Fixes: **dynamic receive window + global budget** (B + bounds pipe memory), **connection cap (100) +
  fast idle reaping (20 s)** (A).
- **Throughput fix = the dynamic window's RE-OPEN**, not buffer sizes. A correct proactive window-update keeps
  the device's send window sliding → **upload 38 Mbps / download 50 Mbps** (was ~2/4). Proven by A/B test.
- **Native write is now batched** (Part 4): `SendPacketsAsync` flushes up to 32 packets per
  `NEPacketTunnelFlow.WritePackets` instead of one native call per packet — fewer marshaling crossings on the
  download hot path. (Also corrected a wrong "concurrent `WritePacket` race" verdict — see Part 4.)

---

## Part 1 — The memory floor: Mono → CoreCLR

- **`phys_footprint ≈ anon + comp`** (dirty + compressed anonymous). The ~50 MB of `code` (file-backed
  AOT/runtime) is **NOT counted** by jetsam → trimming/linking/AOT changes can't move the footprint.
- Of the floor, only ~6 MB was the managed GC heap; the rest (~36 MB) was **non-managed native** (Mono runtime,
  TLS, stacks). So **GC/csproj knobs (`soft-heap-limit`, `ConserveMemory`) can't help** — they only touch the
  ~6 MB managed slice. (They're still required to keep the managed heap from spiking; don't remove them.)
- **NativeAOT** would shrink the floor but for iOS it only engages via whole-app publish and needs the entire
  closure (incl. the SPA web server + reflection-based JSON) to be trim/AOT-safe — a large detour. **Superseded
  by CoreCLR.**
- **.NET 11 / CoreCLR** (the new default mobile runtime in net11 P4) cut the floor ~in half and, crucially, its
  real generational GC **absorbs the per-packet allocation churn that Mono spiked on** → flat under load.
- **CoreCLR boot fix (required):** under CoreCLR's managed-static registrar, pointing
  `NSExtensionPrincipalClass` at the core `IosVpnService` crashes on launch:
  `ObjCRuntime.RuntimeException: Could not find the assembly VpnHood.Core.Client.Devices.Ios`. Fix = a thin
  local **`src/Apps/Client.Ios.Extension/PacketTunnelProvider.cs`** subclass of `IosVpnService` (roots the core assembly), with
  `Info.plist NSExtensionPrincipalClass = PacketTunnelProvider`. (Mono tolerated its absence.)

| | Mono (net10) | **CoreCLR (net11)** |
|--|--|--|
| Connect floor (idle) | ~42 MB | **~23 MB** |
| Packet mode, 32 conn, 1.1 GB | spikes → jetsam | **flat 26.4 MB** |
| Production packet mode, 133 MB up | n/a | **flat ~30 MB** |

---

## Part 2 — Proxy mode (`UseTcpProxy=true`) memory

Each proxied TCP flow is **its own dedicated TLS connection to the server** (not multiplexed over the packet
channel — so `PacketChannelBufferSize`/`MaxPacketChannelCount` are irrelevant in proxy mode), plus a
`LocalTcpConnection` (16 KB retx) + a reassembly pipe. **~180 KB structural per flow.**

- **Real concurrency is ~20× the intuition:** full-tunnel routes *every* app/OS connection, not just the page
  you see. Measured **peakConn 116** (speedtest) / **192** (browsing). `est=conn` always → these are *real open*
  flows (mostly idle HTTP keep-alives), **not** un-reaped zombies. Memory scales with **connection count**.
- **Mode A — connection pile-up:** ~190 × ~180 KB → jetsam. **Fix:** `MaxConnections = 40` (excess flows RST,
  retried — a cap of 100 was initially tested, but under heavy browsing it still let memory drift up too close to the Jetsam limit. 40 keeps a healthy margin) + `IdleTimeout 2 min → 20 s` (`IdleCheckInterval 5 s`) so finished keep-alives free memory fast.
- **Mode B — upload-burst pipe balloon:** a fixed receive window never throttled a fast uploader, so data backed
  up unboundedly in the reassembly pipes + downstream write buffers (`pipeBuf` 8.4 MB, `gcLive` 34 MB → jetsam).
  **Fix:** the dynamic receive window + global budget (below) bounds it (`pipeBuf` → ~0).

---

## Part 3 — Throughput (the upload-speed fix) — the subtle one

**The ~2 Mbps upload was a real goodput limit** (the pre-fix "fast upload" was an illusion — data piled into the
unbounded pipe = the memory bug; real delivery was always ~2 Mbps). **Two suspects were tested on-device and
RULED OUT — neither changed it:** (1) the receive window (16 KB → 64 KB), (2) the copy buffer
`StreamProxyBufferSize` (2 KB → 32 KB).

**The real fix was the dynamic window's RE-OPEN logic.** TCP flow control has two halves:
- **Shrink** — advertise a smaller window as the pipe fills; at full, advertise 0 → the device **stops**.
- **Re-open** — as we drain, advertise a bigger window. **A stopped (zero-window) sender sends nothing, so it
  never sees a normal ACK; without a *proactive* window-update it just sends slow persist-probes → throughput
  collapses.** The first implementation only sent the proactive update after a *full* window close
  (`_windowClosed && win ≥ 4 KB`), which rarely fired under a steady upload → the device throttled/stalled
  → ~2 Mbps. **This is why enlarging the window/buffer looked useless — the broken re-open was the real cap.**

**The fix** (in `LocalTcpConnection`): track the last window advertised (`_lastAdvertisedWindow`, set by
`UpdateAdvertisedWindow()` on every send) and send a window-update ACK whenever the window **slides open by
≥16 KB** (`currentWin − lastWin ≥ 16384`) — standard TCP window-update. The device's send window now stays
fresh and large → full speed. Memory stays bounded because the *shrink* half (global budget) is unchanged.

**A/B proof (2026-06-19):** with that one clause disabled (everything else identical), upload **stalled** —
`up` frozen at 10.5 MB while download flowed to 75 MB, and the stalled flows were reaped by the 20 s idle
timeout (`conn` 51→12 = "connection dropped"); no crash. Re-enabled → **upload 38 Mbps**.

**Result (good build):** **upload 38 Mbps / download 50 Mbps**, footprint peak **34–38 MB** under load (with a connection cap of 40), `pipeBuf=0`. Note: 64 KB window sufficed (no window scaling needed) *because* the re-open keeps it sliding near full, so the effective BDP is covered on this path.

---

## Part 4 — Native tun write path (batched)

`IosVpnAdapter` writes inbound packets (server → device) into iOS via `NEPacketTunnelFlow.WritePackets`.
Each such call **marshals managed arrays into temporary native `NSArray`/`NSData` and allocates autoreleased
native temporaries**. Core's `PacketTransportBase` already drains its send channel in bulk (a single consumer,
up to `QueueCapacity` ≈ 255 packets per cycle) and hands the whole list to `SendPacketsAsync`.

- **Old path:** the adapter overrode only the per-packet `WritePacket`, so the base `SendPacketsAsync` looped
  and issued **one native `WritePackets([1 packet])` per packet** → one marshaling crossing + autorelease churn
  per packet. On a TCP-proxy download (many back-to-back MSS packets) that is the hot path.
- **New path (`feat/ios-tun-batch-write`):** the adapter overrides `SendPacketsAsync` directly and flushes the
  drained list to the native flow in **batches of up to 32** (`MaxWriteBatchSize`), using **reused,
  allocation-free arrays** wrapped in **one autorelease pool per drain**. Far fewer native crossings per burst.
  - Partial (final) chunks are handed an **exactly-sized** array (`_partialWrite*Batches[n]`, pre-allocated
    length `n`) because `WritePackets` walks the full array length — a trailing `null` would `SIGABRT` natively.
  - Each `NSData` is `FromBytes`-copied (so the reused `_writeBuffer` is safe to overwrite per slot) and
    **disposed in the same chunk** after the native call, so native peers never accumulate toward jetsam.
  - `WritePacket` (the per-packet override) is now **unreachable** and throws — `SendPacketsAsync` is the only
    write path, mirroring how `ReadPacket` throws in favor of the `ReadPackets` callback.

> **Corrected verdict (was a "double problem"):** an earlier comment asserted that concurrent `WritePacket`
> calls from TCP-proxy pump threads *raced* on the shared write arrays and that the `_writeLock` fixed a data
> race causing `SIGABRT`. That diagnosis was **wrong**. Core's send channel is `SingleReader=true` and this
> transport is non-passthrough, so `SendPacketsAsync`/`WritePacket` is only ever invoked from the **single**
> consumer loop — pump threads merely *enqueue*. The real `SIGABRT` was a **garbled/null `NSData` handed to
> `WritePackets`** (lifetime/marshaling bug), not a thread race. The `_writeLock` is **kept** anyway as cheap
> (once-per-drain, uncontended) defensive serialization in case a direct/passthrough write path is ever added.

## Part 5 — Recent Tuning Experience: Watchdog Aggression & Optimal Thresholds

In July 2026, we encountered an issue where the panic recycler was triggering aggressively (every 20–30 seconds) during speed tests. We analyzed the on-device logs and refined the memory threshold tuning with the following key findings:

1. **Watchdog Aggression Root Cause**:
   The baseline idle memory footprint of the extension is `30.1 MB` to `30.4 MB`. Setting the connection recycle threshold too close to this baseline (initially `41.0 MB`) meant that any normal burst of traffic (which allocates ~10 MB of native packet buffers) would immediately trip the watchdog and sever the connection pool.

2. **Single-Flow vs. Multi-Flow Behavior**:
   - **Large File Downloads**: Standard single-flow file downloads (even gigabyte-scale files) only use 1 or 2 concurrent connections. They run at full speed, maintain a stable memory footprint of `31–34 MB` (far below the gates), and **never** trigger connection recycling.
   - **Concurrency Storms**: Speed tests and web page load stampedes spin up 40+ concurrent TCP flows. This high connection count allocates many native `NWConnection` objects and socket buffers, driving the footprint to `41–45 MB` rapidly.

3. **How We Fixed It**:
   We raised the cascading thresholds by `3.0 MB` to give active traffic breathing room:
   - **`ConnectMemoryLimitMb`**: Raised from `39.0` to `41.0 MB` (stops handshaking new flows).
   - **`AdmissionMemoryLimitMb`**: Raised from `40.0` to `42.0 MB` (silently drops new SYNs).
   - **`PanicFootprintMb`**: Raised from `41.0` to `44.0 MB` (watchdog recycles).
   
   This shifts the panic boundary upwards. It allows normal browsing and large downloads to operate completely uninterrupted, while still maintaining a robust `8.0 MB` safety margin below the hard `52.0 MB` Jetsam limit to absorb native allocation lag and in-flight traffic.

---

## Current configuration (working tree)

**iOS TCP-stack profile** — `src/Core/VpnHood.Core.TcpStack/LocalTcpStackOptions.cs` `Ios`:
- `ReceiveWindowSize=0xFFFF (64 KB)`, **`GlobalReceiveBudget=6 MB`**, `RetxBufferSize=16 KB`.
- **`MaxConnections=40`**, `AcceptQueueCapacity=128`, **`IdleTimeout=20 s`**, **`IdleCheckInterval=5 s`**.
- **`AdmissionMemoryLimitMb=42.0`** — Memory admission gate. When the process footprint matches or exceeds 42.0 MB, new TCP SYNs are dropped silently so the peer's own SYN-retransmit backoff acts as a natural pacing mechanism until memory recedes.

**Connector Service & QUIC Panic Recycler**:
- **Connection Handshake Gate** — `ConnectMemoryLimitMb=41.0` (in `ConnectorService.cs`). When the process footprint reaches 41.0 MB, new server connection handshakes are held, serialized behind a thread-count gate (max 3 concurrent on iOS).
- **Panic Watchdog Recycle** — `PanicFootprintMb=44.0` (in `QuicStreamConnectionFactory.cs`). A dedicated 250 ms watchdog thread runs on iOS. If the footprint reaches 44.0 MB, it triggers `RecycleAll()`, severing and disposing of all active QUIC connections to instantly release native buffers, recovering to baseline in ~130 ms.

**Dynamic window** — `LocalTcpConnection.AdvertisedWindow = min(ReceiveWindowSize − pipeUnread,
GlobalReceiveBudget − totalPipeBuffered)`; `UpdateAdvertisedWindow()` tracks `_lastAdvertisedWindow`;
`OnAppConsumed` sends a window-update when `(_windowClosed && win≥4 KB) || (win−lastWin ≥ 16 KB)`.

**Host** — `src/Apps/Client.Ios/AppDelegate.cs`: `MaxPacketChannelCount=1`, `PacketChannelBufferSize=16 KB`,
`UdpProxyBufferSize=16 KB`, `StreamProxyBufferSize=32 KB`, `TcpKernelBufferSize=64 KB` (bounds split/exclude socket buffers).
TFM `net11.0-ios` on App + Extension + the 3 iOS core libs (Devices.Ios, IosTun, AppLib.Ios.Common).

---

## Diagnostic probe — `Documents/ext-mem.log`
```
HH:mm:ss.fff footprint=MB peak=MB gcLive=MB gcHeap=MB native=MB anon=MB comp=MB code=MB \
  conn=N est=N peakConn=N pipeBuf=MB win=KB maxC=N dn=MB up=MB
```
`footprint ≈ anon + comp`. `conn/est/peakConn` = live/established/peak proxy connections. `pipeBuf` = total
reassembly backlog (Mode-B signal). `up`/`dn` cumulative MB (compute rate from deltas).

---

## Key insight
`memory = per-flow-cost × concurrent-flows`; `throughput = window/RTT` **but only if the window keeps
re-opening.** On a 52 MB budget with concurrent flows the strategy is: **global budgets** (few active
flows get big windows = full speed; many flows bounded in aggregate) + **a correct window re-open** (proactive
window-update so senders never stall) + **fast idle reaping + a connection cap** + **cascading memory gates (41 MB / 42 MB / 44 MB) to prevent Jetsam kills.** CoreCLR provides the baseline headroom that makes it all fit.

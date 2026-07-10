# iOS Network Extension — Memory & Throughput (consolidated)

**Status:** proxy mode working on iOS with good throughput + bounded memory (on .NET 11 preview).
**Investigation:** 2026-06-16 → 2026-06-19 • **Device:** iPhone 11 (A13, 16 KB pages, iOS 26.5),
hardware UDID `00008030-001544500A38802E` • **Builds with:** `~/.dotnet11/dotnet`
(SDK 11.0.100-preview.5, iOS workload `26.5.11546-net11-p5`).

> This file merges the three working docs (`ios-memory-jetsam-investigation`, `ios-net11-coreclr-proxy-speed`,
> and Gemini's `upload-speed-and-memory-stabilization`) into one accurate record. Code changes live in the
> core projects under `Src/Core`; the app glue is under `Src/Apps/{Client,Connect}.Ios[.Extension]`. The
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
  local **`Src/Apps/Client.Ios.Extension/PacketTunnelProvider.cs`** subclass of `IosVpnService` (roots the core assembly), with
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
- **Mode A — connection pile-up:** ~190 × ~180 KB → jetsam. **Fix:** `MaxConnections = 100` (excess flows RST,
  retried) + `IdleTimeout 2 min → 20 s` (`IdleCheckInterval 5 s`) so finished keep-alives free memory fast.
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

**Result (good build):** **upload 38 Mbps / download 50 Mbps**, footprint peak **45.3 MB** even at the 100-conn
cap, `pipeBuf=0`. Note: 64 KB window sufficed (no window scaling needed) *because* the re-open keeps it sliding
near full, so the effective BDP is covered on this path.

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

---

## Current configuration (working tree)

**iOS TCP-stack profile** — `Src/Core/VpnHood.Core.TcpStack/LocalTcpStackOptions.cs` `Ios`:
`ReceiveWindowSize=0xFFFF (64 KB)`, **`GlobalReceiveBudget=6 MB`**, `RetxBufferSize=16 KB`,
**`MaxConnections=100`**, `AcceptQueueCapacity=128`, **`IdleTimeout=20 s`**, **`IdleCheckInterval=5 s`**.

**Dynamic window** — `LocalTcpConnection.AdvertisedWindow = min(ReceiveWindowSize − pipeUnread,
GlobalReceiveBudget − totalPipeBuffered)`; `UpdateAdvertisedWindow()` tracks `_lastAdvertisedWindow`;
`OnAppConsumed` sends a window-update when `(_windowClosed && win≥4 KB) || (win−lastWin ≥ 16 KB)`. Diagnostics
via `TcpStackDiagnostics` (`ActiveDiagnostics` static, read by the probe).

**Host** — `Src/Apps/Client.Ios/AppDelegate.cs`: `MaxPacketChannelCount=1`, `PacketChannelBufferSize=16 KB`,
`UdpProxyBufferSize=16 KB`, `StreamProxyBufferSize=32 KB`, `TcpKernelBufferSize=64 KB`
(2026-07-09: 256 KB → 64 KB. The knob applies to every managed TCP socket via `ConfiguringSocketFactory`,
including the per-flow direct sockets of split/exclude "passthru" flows — one real kernel socket per
excluded flow, unbounded in aggregate unlike the QUIC tunnel windows. At 256 KB a split-country browse
could pin ~40 × 512 KB ≈ 20 MB of socket buffers → jetsam; 64 KB bounds it to ~5 MB worst case). **TEMP:**
`UseTcpProxy=true` forced + `AccessKey=<test key>` — both **DO NOT COMMIT**. TFM `net11.0-ios` on App +
Extension + the 3 iOS core libs (Devices.Ios, IosTun, AppLib.Ios.Common); 28 neutral libs stay `net10.0`.

---

## Diagnostic probe — `Documents/ext-mem.log`
```
HH:mm:ss.fff footprint=MB peak=MB gcLive=MB gcHeap=MB native=MB anon=MB comp=MB code=MB \
  conn=N est=N peakConn=N pipeBuf=MB win=KB maxC=N dn=MB up=MB
```
`footprint ≈ anon + comp`. `conn/est/peakConn` = live/established/peak proxy connections. `pipeBuf` = total
reassembly backlog (Mode-B signal). `up`/`dn` cumulative MB (compute rate from deltas).

**Build / deploy:** see [build-deploy-and-provisioning.md](build-deploy-and-provisioning.md) (net11 / `~/.dotnet11`).
When you change a core TCP-stack lib, also `rm -rf` its `bin/obj` so the appex relinks fresh dlls. Then pull the
probe + crash log:
```bash
xcrun devicectl device copy from --device 00008030-001544500A38802E \
  --domain-type appDataContainer --domain-identifier com.vpnhood.client.ios.networkextension \
  --source Documents --destination .working/pulled
# device crash log (needs sudo): sudo /usr/bin/log collect --device-udid <UDID> --last 10m --output /tmp/d.logarchive
```

---

## Remaining work / open items
1. **Memory margin:** 45.3 MB peak at the 100-conn cap is ~7 MB under the limit; dominant consumer is the
   per-flow structural cost (`gcLive≈29` at 100 conn), not pipes. If pushed harder, lower `MaxConnections` or
   shrink the per-flow server `SslStream`. Validate a heavy browse + speedtest combined stays < 52 MB.
2. **.NET 11 is preview** — a shippable/App-Store build needs **.NET 11 GA**.
3. **Revert temps before committing app code:** `AccessKey → null`; remove forced `UseTcpProxy=true`
   (make UI-controlled); decide whether to keep the diagnostic probe + `TcpStackDiagnostics`.
4. **Finalize knob values** once both green; window scaling appears unnecessary (64 KB + re-open gives full speed).

## Key insight
`memory = per-flow-cost × concurrent-flows`; `throughput = window/RTT` **but only if the window keeps
re-opening.** On a 52 MB budget with 100–200 concurrent flows the strategy is: **global budgets** (few active
flows get big windows = full speed; many flows bounded in aggregate) + **a correct window re-open** (proactive
window-update so senders never stall) + **fast idle reaping + a connection cap**. CoreCLR provides the headroom
that makes it all fit.

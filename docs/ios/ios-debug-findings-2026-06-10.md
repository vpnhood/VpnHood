# iOS Extension Crash Investigation — Findings & Fixes (2026-06-10)

Debugging session results for the iOS Network Extension jetsam/crash hunt in TCP-proxy split-tunnel
mode. The core-repo changes from this session may be reverted — **this document is the durable
record of the real bugs found**, so they can be re-applied independently of all the experimental
tuning that accompanied them.

All file paths below are core projects under `Src/Core` in this repo.

---

## Confirmed bugs (re-apply these even after a revert)

### 1. Thread race in `WritePacket` → SIGABRT crash
**File:** `Src/Core/VpnHood.Core.VpnAdapters.IosTun/IosVpnAdapter.cs`, `WritePacket`
**Symptom:** extension dies with SIGABRT at any memory level (not jetsam). Confirmed by a
symbolicated native crash report: `objc_exception_throw → std::terminate → abort`.
**Cause:** `WritePacket` reuses shared single-instance state (`_writeBuffer`, `_writeDataArray`,
`_writeProtoArray`) and disposes the `NSData` immediately. The user-space TCP stack emits inbound
packets from many connection pump threads concurrently, so concurrent calls raced on these fields
and handed a disposed/garbled `NSData` to `NEPacketTunnelFlow.WritePackets` → uncaught
NSInvalidArgumentException → SIGABRT.
**Fix:** serialize the entire native write with a single `private readonly object _writeLock`;
wrap the whole method body in `lock (_writeLock)`.

### 2. TLS connection dispose-leak on failed HTTP/WebSocket upgrade
**File:** `Src/Core/VpnHood.Core.Client/ConnectorServices/ConnectorService.cs`,
`GetConnectionToServer`
**Symptom:** extension footprint climbs ~3 MB/min until jetsam; diagnostics showed **321
connections finalized without ever being disposed** in one run.
**Cause:** `GetConnectionToServer` creates a raw TLS-authenticated connection, then calls
`CreateHttpConnection` (WebSocket upgrade). When the upgrade throws (e.g. `"Unexpected response."`
when the server returns non-101), the `rawConnection` was abandoned **undisposed** — its native
Apple SecureTransport context stayed resident until a lazy finalizer.
**Fix:**
```csharp
var rawConnection = ... CreateConnection(...);
try {
    var connection = await CreateHttpConnection(rawConnection, contentLength, ct);
    ...
    return connection;
}
catch {
    rawConnection.Dispose();
    throw;
}
```

### 3. Packet-channel recreation retry flood (no backoff) — ALL PLATFORMS
**File:** `Src/Core/VpnHood.Core.Client/ClientSession.cs`, `ManagePacketChannels` (triggered from
`ProcessOutgoingPacket`)
**Symptom:** when the packet channel dies and recreation fails persistently, the client hammers
the server: measured **464 TLS handshakes and 323 `TcpPacketChannel` requests in ~60 s** (~5/s).
Combined with bug #2 every failed attempt leaked a TLS connection → death spiral → jetsam. The
flood itself likely keeps the server rejecting (rate limiting), making the failure self-sustaining.
**Cause:** `ManagePacketChannels` is re-triggered by **every outgoing packet** while
`PacketChannelCount < MaxPacketChannelCount`; there is no delay between failed attempts (the
`_packetChannelLock` only prevents concurrent attempts, not rapid-fire sequential ones).
**Fix:** track consecutive failures + last failure time; on entry, skip if within an exponential
backoff window (1 s, 2 s, 4 s … capped at 15 s). Reset the counter on success. Fields:
`_packetChannelFailCount`, `_packetChannelLastFailTime`.

### 4. Unconditional IPv6 route injection defeats route-level split tunneling
**File:** `Src/Core/VpnHood.Core.VpnAdapters.IosTun/IosVpnAdapter.cs`, `AdapterOpen` (IPv6 block)
**Symptom:** with device-level split configured to exclude everything (tunnel routes = DNS /32s
only), browser traffic still entered the extension (`pk>0`) and jetsam deaths continued. Safari
prefers IPv6.
**Cause:** on networks without native IPv6 the adapter **always** injects `::/0` + `2000::/3`
into `IPv6Settings.IncludedRoutes` ("convince iOS that AAAA works"), ignoring the include list.
Correct for full-tunnel; silently captures ALL IPv6 in split mode.
**Fix:** inject only when core actually requested broad v6 coverage:
```csharp
var wantsBroadV6 = _ipv6Routes.Any(r => r.PrefixLength < 64);
var includes = IsIpVersionSupported(IpVersion.IPv6) || !wantsBroadV6
    ? _ipv6Routes
    : new List<IpNetwork> { IpNetwork.AllV6, IpNetwork.AllGlobalUnicastV6 }.Concat(_ipv6Routes);
```

### 5. DNS unreachable in device-level split mode
**File:** `Src/Core/VpnHood.Core.Client/ClientSessionBuilder.cs` (where `VpnAdapterOptions` is built)
**Symptom:** with `UseSplitIpViaDevice` + `DeviceExcludes = "0.0.0.0/0\n::/0"`, the adapter include
routes are empty, so the system cannot deliver DNS queries to the tunnel's DNS servers — name
resolution silently breaks.
**Fix:** always union the DNS server IPs into the adapter routes:
```csharp
var includeNetworksWithDns = sessionIncludeIpRangesByDevice.ToIpNetworks()
    .Concat(dnsConfig.DnsServers.Select(ip => new IpNetwork(ip, ip.IsV4() ? 32 : 128)))
    .Distinct()
    .ToArray();
// pass includeNetworksWithDns as VpnAdapterOptions.IncludeNetworks
```
(Companion to the earlier `ClientPacketHandler` change that force-includes DNS packets in app-level
split mode.)

---

## Superseded conclusion (kept for history)

This session concluded that proxy/bridge mode (`UseTcpProxy=true`) was **unfixable** on the 52 MB
budget — Apple's libnetwork allocates **+10–12 MB inside the extension within 100–300 ms** during
browser connection storms (page open/refresh), and no concurrency cap, bandwidth clamp, admission
gate, or `soft-heap-limit` stopped the atomic leap — so **route-level split**
(`UseSplitIpViaDevice=true`, `DeviceExcludes="0.0.0.0/0\n::/0"`, packet mode) was named the
production answer.

**That conclusion is now obsolete.** Proxy mode was subsequently made viable on iOS: the real floor
was the **Mono runtime** (halved by **.NET 11 / CoreCLR**, whose generational GC also absorbs the
per-packet allocation churn Mono spiked on), plus a **dynamic receive window + global budget +
100-connection cap + 20 s idle reaping**. Bugs #1–#5 above remain valid and were kept; the
"route-level split is the only answer" recommendation was not. See
[ios-extension-memory-and-throughput.md](ios-extension-memory-and-throughput.md) for the current record.

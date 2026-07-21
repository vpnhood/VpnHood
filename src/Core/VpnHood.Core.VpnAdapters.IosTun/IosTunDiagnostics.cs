using System.Net;
using Microsoft.Extensions.Logging;
using NetworkExtension;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.VpnAdapters.IosTun;

/// <summary>
/// iOS TUN adapter <b>investigation instrumentation</b>, owned by this project (mirrors
/// <c>TcpStackDiagnostics</c>): it holds the cumulative traffic counters and the freeze locator; every
/// mutating method is a no-op when <see cref="Enabled"/> is false, so call sites stay clean, and it costs
/// nothing in production.
/// </summary>
/// <remarks>
/// The host's memory probe reads the public snapshot properties below to correlate phys_footprint with
/// traffic; it does not own these counters.
/// <para>Off in production; there is no dedicated switch — <see cref="Enabled"/> is computed from
/// <c>VhLogger.MinLogLevel</c>, so below-Information logging (e.g. the <c>/log:debug</c> DebugCommand in
/// the app UI, flowing to the Extension via <c>ClientOptions.LogServiceOptions</c>) enables all the iOS
/// diagnostics together.</para>
/// </remarks>
public static class IosTunDiagnostics
{
    // ---- backing fields --------------------------------------------------------------------------
    private static long _inboundBytes;
    private static long _outboundBytes;
    private static long _lastReadTicks;
    private static long _lastWriteTicks;
    private static long _maxTunWriteMs;
    private static long _readCallbackCount;
    private static long _readPacketCount;
    private static long _maxReadBatchSize;

    // ---- public state ----------------------------------------------------------------------------
    /// <summary>Read-only master gate: on whenever the effective log level is below Information.</summary>
    public static bool Enabled => VhLogger.MinLogLevel < LogLevel.Information;

    /// <summary>Cumulative bytes written inbound (server → device) through the TUN adapter.</summary>
    public static long InboundBytes => Interlocked.Read(ref _inboundBytes);
    /// <summary>Cumulative bytes read outbound (device → server) from the TUN adapter.</summary>
    public static long OutboundBytes => Interlocked.Read(ref _outboundBytes);
    /// <summary><c>Environment.TickCount64</c> at the last outbound read callback (0 = never).</summary>
    public static long LastReadTicks => Volatile.Read(ref _lastReadTicks);
    /// <summary><c>Environment.TickCount64</c> after the last completed TUN write drain (0 = never).</summary>
    public static long LastWriteTicks => Volatile.Read(ref _lastWriteTicks);
    /// <summary>Cumulative native TUN read callbacks received.</summary>
    public static long ReadCallbackCount => Interlocked.Read(ref _readCallbackCount);
    /// <summary>Cumulative packets delivered by native TUN read callbacks.</summary>
    public static long ReadPacketCount => Interlocked.Read(ref _readPacketCount);

    // ---- traffic counters ------------------------------------------------------------------------
    /// <summary>Adds to the inbound (download) byte counter. No-op unless <see cref="Enabled"/>.</summary>
    public static void AddInboundBytes(long bytes)
    {
        if (Enabled)
            Interlocked.Add(ref _inboundBytes, bytes);
    }

    /// <summary>Adds to the outbound (upload) byte counter. No-op unless <see cref="Enabled"/>.</summary>
    public static void AddOutboundBytes(long bytes)
    {
        if (Enabled)
            Interlocked.Add(ref _outboundBytes, bytes);
    }

    // ---- freeze locator --------------------------------------------------------------------------
    /// <summary>Records an outbound native read callback and its batch size.</summary>
    public static void MarkTunReadCallback(int packetCount)
    {
        if (!Enabled)
            return;

        Volatile.Write(ref _lastReadTicks, Environment.TickCount64);
        Interlocked.Increment(ref _readCallbackCount);
        Interlocked.Add(ref _readPacketCount, packetCount);

        long previous;
        while (packetCount > (previous = Volatile.Read(ref _maxReadBatchSize)) &&
               Interlocked.CompareExchange(ref _maxReadBatchSize, packetCount, previous) != previous) { }
    }

    /// <summary>Largest native read batch since the last call; reading resets it to 0.</summary>
    public static long TakeMaxReadBatchSize() => Interlocked.Exchange(ref _maxReadBatchSize, 0);

    /// <summary>
    /// Returns a start timestamp for bracketing the native write drain, or 0 when disabled so the paired
    /// <see cref="EndTunWrite"/> becomes a no-op.
    /// </summary>
    public static long BeginTiming() => Enabled ? Environment.TickCount64 : 0;

    /// <summary>Records a completed TUN write drain: stamps the time and tracks the worst duration.</summary>
    public static void EndTunWrite(long startTimestamp)
    {
        if (!Enabled) return;
        var now = Environment.TickCount64;
        Volatile.Write(ref _lastWriteTicks, now);
        var elapsed = now - startTimestamp;
        long prev;
        while (elapsed > (prev = Volatile.Read(ref _maxTunWriteMs)) &&
               Interlocked.CompareExchange(ref _maxTunWriteMs, elapsed, prev) != prev) { }
    }

    /// <summary>Worst single TUN write drain (ms) since the last call; reading resets it to 0.</summary>
    public static long TakeMaxTunWriteMs() => Interlocked.Exchange(ref _maxTunWriteMs, 0);

    // ---- route dump ------------------------------------------------------------------------------
    /// <summary>
    /// Dumps the routes/addresses/DNS the adapter is about to install to <c>Documents/ext-route-dump.txt</c>
    /// so the host can verify what actually reaches iOS ("connected but no traffic" diagnosis). No-op unless
    /// <see cref="Enabled"/>. Best-effort: never throws.
    /// </summary>
    public static async Task WriteRouteDumpAsync(
        IPAddress remoteAddress, int? mtu, NEPacketTunnelNetworkSettings networkSettings,
        IReadOnlyList<IpNetwork> ipv4Networks, IReadOnlyList<IpNetwork> ipv4Routes,
        IReadOnlyList<IpNetwork> ipv6Networks, IReadOnlyList<IpNetwork> ipv6Routes,
        IReadOnlyList<IPAddress> dnsServers, CancellationToken cancellationToken)
    {
        if (!Enabled)
            return;

        try {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var inc4 = networkSettings.IPv4Settings?.IncludedRoutes?.Select(r => $"{r.DestinationAddress}/{r.DestinationSubnetMask}") ?? [];
            var exc4 = networkSettings.IPv4Settings?.ExcludedRoutes?.Select(r => $"{r.DestinationAddress}/{r.DestinationSubnetMask}") ?? [];
            var inc6 = networkSettings.IPv6Settings?.IncludedRoutes?.Select(r => $"{r.DestinationAddress}/{r.DestinationNetworkPrefixLength.Int32Value}") ?? [];
            var exc6 = networkSettings.IPv6Settings?.ExcludedRoutes?.Select(r => $"{r.DestinationAddress}/{r.DestinationNetworkPrefixLength.Int32Value}") ?? [];
            await File.WriteAllTextAsync(Path.Combine(docs, "ext-route-dump.txt"),
                $"AdapterOpen at {DateTime.UtcNow:O}\n" +
                $"remoteAddress(ServerAddress)={remoteAddress}\n" +
                $"mtu={mtu}\n" +
                $"v4 address({ipv4Networks.Count}): {string.Join(", ", ipv4Networks.Select(n => n.ToString()))}\n" +
                $"v4 routes-from-core({ipv4Routes.Count}): {string.Join(", ", ipv4Routes.Select(r => $"{r.Prefix}/{r.PrefixLength}"))}\n" +
                $"v4 INCLUDED-applied: {string.Join(", ", inc4)}\n" +
                $"v4 EXCLUDED-applied: {string.Join(", ", exc4)}\n" +
                $"v6 address({ipv6Networks.Count}): {string.Join(", ", ipv6Networks.Select(n => n.ToString()))}\n" +
                $"v6 routes-from-core({ipv6Routes.Count}): {string.Join(", ", ipv6Routes.Select(r => $"{r.Prefix}/{r.PrefixLength}"))}\n" +
                $"v6 INCLUDED-applied: {string.Join(", ", inc6)}\n" +
                $"v6 EXCLUDED-applied: {string.Join(", ", exc6)}\n" +
                $"dns({dnsServers.Count}): {string.Join(", ", dnsServers.Select(d => d.ToString()))}\n", cancellationToken);
        }
        catch { /* best-effort */ }
    }
}

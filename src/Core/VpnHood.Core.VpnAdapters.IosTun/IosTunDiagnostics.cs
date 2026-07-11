using System.Net;
using NetworkExtension;
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
/// <para>Off in production; seeded from the <c>VH_IOS_DIAGNOSTICS</c> environment variable (any of
/// <c>1</c>/<c>true</c>/<c>yes</c>) so one switch enables all the iOS diagnostics together.</para>
/// </remarks>
public static class IosTunDiagnostics
{
    // ---- backing fields --------------------------------------------------------------------------
    private static long _inboundBytes;
    private static long _outboundBytes;
    private static long _lastReadTicks;
    private static long _lastWriteTicks;
    private static long _maxTunWriteMs;

    // ---- public state ----------------------------------------------------------------------------
    /// <summary>Master switch. Defaults to <c>false</c> (production); seeded from <c>VH_IOS_DIAGNOSTICS</c>.</summary>
    public static bool Enabled { get; set; } = ReadEnvDefault();

    /// <summary>Cumulative bytes written inbound (server → device) through the TUN adapter.</summary>
    public static long InboundBytes => Interlocked.Read(ref _inboundBytes);
    /// <summary>Cumulative bytes read outbound (device → server) from the TUN adapter.</summary>
    public static long OutboundBytes => Interlocked.Read(ref _outboundBytes);
    /// <summary><c>Environment.TickCount64</c> at the last outbound read callback (0 = never).</summary>
    public static long LastReadTicks => Volatile.Read(ref _lastReadTicks);
    /// <summary><c>Environment.TickCount64</c> after the last completed TUN write drain (0 = never).</summary>
    public static long LastWriteTicks => Volatile.Read(ref _lastWriteTicks);

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
    /// <summary>Stamps the last outbound-read-callback time. No-op unless <see cref="Enabled"/>.</summary>
    public static void MarkTunReadCallback()
    {
        if (Enabled)
            Volatile.Write(ref _lastReadTicks, Environment.TickCount64);
    }

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

    // Seed Enabled from the VH_IOS_DIAGNOSTICS env var (any of 1/true/yes) so one switch turns on all
    // the iOS diagnostics for a dev/simulator run without a code change.
    private static bool ReadEnvDefault()
    {
        try {
            var value = Environment.GetEnvironmentVariable("VH_IOS_DIAGNOSTICS");
            return value is "1" or "true" or "True" or "TRUE" or "yes" or "YES";
        }
        catch {
            return false;
        }
    }
}

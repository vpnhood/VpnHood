using System.Runtime.CompilerServices;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Core.Tunneling;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Server;

// A session's limits on what its client sends, set by the session options; the server's NetFilter is the same for
// every session. A struct whose packet check is inlined, so a packet that no limit covers costs comparisons only
internal readonly struct NetProtector(SessionOptions options, LogScope logScope) : IDisposable
{
    private readonly WindowRateLimiter _udpDnsLimiter = new(options.MaxUdpDnsPacketsPerSecondValue, TimeSpan.FromSeconds(1));

    private readonly EventReporter _udpDnsLimitReporter = new(
        "Some DNS packets have been dropped by MaxUdpDnsPacketsPerSecond.", GeneralEventId.NetProtect, logScope);

    // false for a packet over its limit
    [MethodImpl(MethodImplOptions.AggressiveInlining)] // per packet; RyuJIT inlines this size only when its heuristic agrees
    public bool VerifyPacket(IpProtocol protocol, IpEndPointValue destinationEndPoint)
    {
        // DNS over UDP, whatever sends it, the adapter or the proxy: a flood of it gets the server's address reported
        if (protocol == IpProtocol.Udp && DnsPorts.IsDnsPort(destinationEndPoint.Port))
            return TryAcquire(_udpDnsLimiter, _udpDnsLimitReporter);

        return true;
    }

    private static bool TryAcquire(WindowRateLimiter limiter, EventReporter limitReporter)
    {
        if (limiter.TryAcquire())
            return true;

        limitReporter.Raise();
        return false;
    }

    public void Dispose()
    {
        _udpDnsLimitReporter.Dispose();
    }
}

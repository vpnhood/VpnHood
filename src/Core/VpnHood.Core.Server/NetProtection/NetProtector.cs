using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Tunneling;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Server.NetProtection;

// A session's limits on what its client sends; the server's NetFilter is the same for every session
internal sealed class NetProtector(NetProtectorOptions options) : IDisposable
{
    private const byte TcpSynFlag = 0b0000_0010;
    private const byte TcpAckFlag = 0b0001_0000;

    private readonly WindowRateLimiter _udpDnsLimiter = new(options.MaxUdpDnsPacketsPerSecond, TimeSpan.FromSeconds(1));

    private readonly EventReporter _udpDnsLimitReporter = new(
        "Some DNS packets have been dropped by MaxUdpDnsPacketsPerSecond.", GeneralEventId.NetProtect, options.LogScope);

    private readonly WindowRateLimiter _tcpConnectLimiter = new(options.MaxTcpConnectsPerSecond, TimeSpan.FromSeconds(1));

    private readonly EventReporter _tcpConnectLimitReporter = new(
        "Some TCP connections have been refused by MaxTcpConnectsPerSecond.", GeneralEventId.NetProtect, options.LogScope);

    private readonly NetScanDetector? _netScanDetector =
        options is { NetScanLimit: { } netScanLimit, NetScanTimeout: { } netScanTimeout }
            ? new NetScanDetector(netScanLimit, netScanTimeout)
            : null;

    private readonly EventReporter _netScanReporter = new(
        "NetScan protector does not allow this request.", GeneralEventId.NetProtect, options.LogScope);

    // its reporter takes each refusal once
    public int NetScanErrorCount => _netScanReporter.TotalEventCount;

    // false for a packet over its limit
    public bool VerifyPacket(IpPacket ipPacket, IpProtocol protocol, IpEndPointValue destinationEndPoint)
    {
        // a TCP connection's first packet, SYN without ACK, as the TUN takes it; a request to the TCP proxy is checked
        // where the session takes it. The header is the one the destination was read from
        if (protocol == IpProtocol.Tcp) {
            var tcpPacket = ipPacket.PayloadPacket as TcpPacket ?? ipPacket.ExtractTcp();
            return (tcpPacket.Flags & (TcpSynFlag | TcpAckFlag)) != TcpSynFlag || VerifyTcpConnect(destinationEndPoint);
        }

        // DNS over UDP, whatever sends it, the adapter or the proxy: a flood of it gets the server's address reported
        if (protocol == IpProtocol.Udp && DnsPorts.IsDnsPort(destinationEndPoint.Port))
            return TryAcquire(_udpDnsLimiter, _udpDnsLimitReporter);

        return true;
    }

    // false while its network's endpoints are over NetScanLimit; a refused endpoint counts too, until it expires
    public bool VerifyNetScan(IpEndPointValue remoteEndPoint)
    {
        if (_netScanDetector == null || _netScanDetector.Verify(remoteEndPoint))
            return true;

        _netScanReporter.Raise();
        return false;
    }

    // false for a new TCP connection over MaxTcpConnectsPerSecond
    public bool TryAcquireTcpConnect()
    {
        return TryAcquire(_tcpConnectLimiter, _tcpConnectLimitReporter);
    }

    public int? GetNetScanBurstCount(IpEndPointValue remoteEndPoint)
    {
        return _netScanDetector?.GetBurstCount(remoteEndPoint);
    }

    // the cheap rate first, so a flood over it costs NetScan's lookups nothing
    private bool VerifyTcpConnect(IpEndPointValue destinationEndPoint)
    {
        return TryAcquireTcpConnect() && VerifyNetScan(destinationEndPoint);
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
        _tcpConnectLimitReporter.Dispose();
        _netScanReporter.Dispose();
        _netScanDetector?.Dispose();
    }
}

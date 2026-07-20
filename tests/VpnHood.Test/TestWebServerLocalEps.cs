using System.Net;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Test.Providers;

namespace VpnHood.Test;

public class TestWebServerLocalEps(TestIps testIps)
{
    public IPEndPoint HttpsV4EndPoint1 { get; } = AllocateFreeTcpEndPoint(testIps.LocalTestIps[0]);
    public IPEndPoint HttpsV4EndPoint2 { get; } = AllocateFreeTcpEndPoint(testIps.LocalTestIps[1]);

    public IPEndPoint HttpV4EndPoint1 { get; } = AllocateFreeTcpEndPoint(testIps.LocalTestIps[0]);
    public IPEndPoint HttpV4EndPoint2 { get; } = AllocateFreeTcpEndPoint(testIps.LocalTestIps[1]);

    public IPEndPoint UdpEchoEndPoint1 { get; } = AllocateFreeUdpEndPoint(testIps.LocalTestIps[0]);
    public IPEndPoint UdpEchoEndPoint2 { get; } = AllocateFreeUdpEndPoint(testIps.LocalTestIps[1]);
    public IPEndPoint UdpEchoEndPoint3 { get; } = AllocateFreeUdpEndPoint(testIps.LocalTestIps[1]);
    public IPEndPoint UdpEchoEndPoint1V6 { get; } = AllocateFreeUdpEndPoint(testIps.LocalTestIpV6);
    public IPEndPoint UdpEchoEndPoint2V6 { get; } = AllocateFreeUdpEndPoint(testIps.LocalTestIpV6);

    public IPEndPoint QuicEndPoint1 { get; } = AllocateFreeUdpEndPoint(testIps.LocalTestIps[0]);
    public IPEndPoint QuicEndPoint2 { get; } = AllocateFreeUdpEndPoint(testIps.LocalTestIps[0]);
    public IPEndPoint QuicUploadEndPoint1 { get; } = AllocateFreeUdpEndPoint(testIps.LocalTestIps[0]);
    public IPEndPoint QuicDownloadEndPoint1 { get; } = AllocateFreeUdpEndPoint(testIps.LocalTestIps[0]);

    public IPEndPoint TcpDataEndPoint1 { get; } = AllocateFreeTcpEndPoint(testIps.LocalTestIps[0]);
    public IPEndPoint TcpUploadEndPoint1 { get; } = AllocateFreeTcpEndPoint(testIps.LocalTestIps[0]);
    public IPEndPoint TcpDownloadEndPoint1 { get; } = AllocateFreeTcpEndPoint(testIps.LocalTestIps[0]);
    public IPEndPoint UdpUploadEndPoint1 { get; } = AllocateFreeUdpEndPoint(testIps.LocalTestIps[0]);
    public IPEndPoint UdpDownloadEndPoint1 { get; } = AllocateFreeUdpEndPoint(testIps.LocalTestIps[0]);

    public IPEndPoint HttpV4EndPointBlockedClient { get; } = AllocateFreeTcpEndPoint(testIps.LocalBlockedClientIpAddress);
    public IPEndPoint HttpV4EndPointBlockedServer { get; } = AllocateFreeTcpEndPoint(testIps.LocalBlockedServerIpAddress);
    public IPEndPoint UdpNsEchoEndPoint1 { get; } = new(testIps.LocalNsTestIp, 53);
    public IPEndPoint HttpV4EndPointRefused1 { get; } = AllocateFreeTcpEndPoint(testIps.LocalTestIps[0]);

    public Uri HttpsUrl1 => new($"https://{HttpsV4EndPoint1}/file1");
    public Uri HttpsUrl2 => new($"https://{HttpsV4EndPoint2}/file1");
    public Uri HttpUrl1 => new($"http://{HttpV4EndPoint1}/file1");
    public Uri HttpUrl2 => new($"http://{HttpV4EndPoint2}/file2");
    public Uri QuicUrl1 => new($"https://{QuicEndPoint1}/file1");
    public Uri QuicUrl2 => new($"https://{QuicEndPoint2}/file2");

    // machine-wide counters so parallel test hosts never allocate the same port; the free check
    // alone is not enough because ports are bound after allocation, not at allocation time.
    // Counter ports also stay below the OS ephemeral range, so outgoing connections can never
    // steal an allocated-but-not-yet-bound port.
    public static IPEndPoint AllocateFreeTcpEndPoint(IPAddress address)
    {
        while (true) {
            var port = CrossProcessCounter.Next("TcpPort", first: 15000, last: 45000);
            var ep = VhUtils.GetFreeTcpEndPoint(address, port);
            if (ep.Port == port) return ep;
        }
    }

    public static IPEndPoint AllocateFreeUdpEndPoint(IPAddress address)
    {
        while (true) {
            var port = CrossProcessCounter.Next("UdpPort", first: 25000, last: 49000);
            var ep = VhUtils.GetFreeUdpEndPoint(address, port);
            if (ep.Port == port) return ep;
        }
    }

    // Use dynamic ports to avoid port conflicts between sequential test runs
    // must be 127.0.0.1 for quic to work on loopback adapter
    // NS echo must listen on the well-known DNS port (53) for DNS detection, so it uses a
    // dedicated loopback IP where port 53 is free instead of a shared test IP

    public IReadOnlyList<IPEndPoint> AllHttpEndPoints => [
        HttpV4EndPoint1,
        HttpV4EndPoint2,
        HttpV4EndPointBlockedClient,
        HttpV4EndPointBlockedServer,
    ];

    public IReadOnlyList<IPEndPoint> AllHttpsEndPoints => [
        HttpsV4EndPoint1, 
        HttpsV4EndPoint2
    ];

    public IReadOnlyList<IPEndPoint> AllQuicEndPoints => [
        QuicEndPoint1,
        QuicEndPoint2
    ];

    public IReadOnlyList<IPEndPoint> AllQuicUploadEndPoints => [
        QuicUploadEndPoint1
    ];

    public IReadOnlyList<IPEndPoint> AllQuicDownloadEndPoints => [
        QuicDownloadEndPoint1
    ];

    public IReadOnlyList<IPEndPoint> AllTcpDataEndPoints => [
        TcpDataEndPoint1
    ];

    public IReadOnlyList<IPEndPoint> AllTcpUploadEndPoints => [
        TcpUploadEndPoint1
    ];

    public IReadOnlyList<IPEndPoint> AllTcpDownloadEndPoints => [
        TcpDownloadEndPoint1
    ];

    public IReadOnlyList<IPEndPoint> AllUdpEchoEndPoints => [
        UdpEchoEndPoint1,
        UdpEchoEndPoint2,
        UdpEchoEndPoint3,
        UdpEchoEndPoint1V6,
        UdpEchoEndPoint2V6,
        UdpNsEchoEndPoint1
    ];

    public IReadOnlyList<IPEndPoint> AllUdpUploadEndPoints => [
        UdpUploadEndPoint1
    ];

    public IReadOnlyList<IPEndPoint> AllUdpDownloadEndPoints => [
        UdpDownloadEndPoint1
    ];
}

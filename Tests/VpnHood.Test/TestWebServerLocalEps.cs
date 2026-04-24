using System.Net;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Test.Providers;

namespace VpnHood.Test;

public class TestWebServerLocalEps
{
    public IPEndPoint HttpsV4EndPoint1 { get; }
    public IPEndPoint HttpsV4EndPoint2 { get; }

    public IPEndPoint HttpV4EndPoint1 { get; }
    public IPEndPoint HttpV4EndPoint2 { get; }

    public IPEndPoint UdpEchoEndPoint1 { get; }
    public IPEndPoint UdpEchoEndPoint2 { get; }
    public IPEndPoint UdpEchoEndPoint3 { get; }
    public IPEndPoint UdpEchoEndPoint1V6 { get; }
    public IPEndPoint UdpEchoEndPoint2V6 { get; }

    public IPEndPoint QuicEndPoint1 { get; } 
    public IPEndPoint QuicEndPoint2 { get; } 

    public IPEndPoint TcpDataEndPoint1 { get; }

    public IPEndPoint HttpV4EndPointBlockedClient { get; }
    public IPEndPoint HttpV4EndPointBlockedServer { get; }
    public IPEndPoint UdpNsEchoEndPoint1 { get; }
    public IPEndPoint HttpV4EndPointRefused1 { get; }

    public Uri HttpsUrl1 => new($"https://{HttpsV4EndPoint1}/file1");
    public Uri HttpsUrl2 => new($"https://{HttpsV4EndPoint2}/file1");
    public Uri HttpUrl1 => new($"http://{HttpV4EndPoint1}/file1");
    public Uri HttpUrl2 => new($"http://{HttpV4EndPoint2}/file2");
    public Uri QuicUrl1 => new($"https://{QuicEndPoint1}/file1");
    public Uri QuicUrl2 => new($"https://{QuicEndPoint2}/file2");

    private int _nextTcpPort = 15000;
    private int _nextUdpPort = 25000;

    private IPEndPoint AllocateFreeTcpEndPoint(IPAddress address)
    {
        while (true) {
            var port = Interlocked.Increment(ref _nextTcpPort);
            var ep = VhUtils.GetFreeTcpEndPoint(address, port);
            if (ep.Port == port) return ep;
        }
    }

    private IPEndPoint AllocateFreeUdpEndPoint(IPAddress address)
    {
        while (true) {
            var port = Interlocked.Increment(ref _nextUdpPort);
            var ep = VhUtils.GetFreeUdpEndPoint(address, port);
            if (ep.Port == port) return ep;
        }
    }

    public TestWebServerLocalEps(TestIps testIps)
    {
        // Use dynamic ports to avoid port conflicts between sequential test runs
        HttpsV4EndPoint1 = AllocateFreeTcpEndPoint(testIps.LocalTestIps[0]);
        HttpsV4EndPoint2 = AllocateFreeTcpEndPoint(testIps.LocalTestIps[1]);

        HttpV4EndPoint1 = AllocateFreeTcpEndPoint(testIps.LocalTestIps[0]);
        HttpV4EndPoint2 = AllocateFreeTcpEndPoint(testIps.LocalTestIps[1]);

        UdpEchoEndPoint1 = AllocateFreeUdpEndPoint(testIps.LocalTestIps[0]);
        UdpEchoEndPoint2 = AllocateFreeUdpEndPoint(testIps.LocalTestIps[1]);
        UdpEchoEndPoint3 = AllocateFreeUdpEndPoint(testIps.LocalTestIps[1]);
        UdpEchoEndPoint1V6 = AllocateFreeUdpEndPoint(testIps.LocalTestIpV6);
        UdpEchoEndPoint2V6 = AllocateFreeUdpEndPoint(testIps.LocalTestIpV6);

        // must be 127.0.0.1 for quic to work on loopback adapter
        QuicEndPoint1 = AllocateFreeUdpEndPoint(testIps.LocalTestIps[0]);
        QuicEndPoint2 = AllocateFreeUdpEndPoint(testIps.LocalTestIps[0]);

        TcpDataEndPoint1 = AllocateFreeTcpEndPoint(testIps.LocalTestIps[0]);

        HttpV4EndPointBlockedClient = AllocateFreeTcpEndPoint(testIps.LocalBlockedClientIpAddress);
        HttpV4EndPointBlockedServer = AllocateFreeTcpEndPoint(testIps.LocalBlockedServerIpAddress);
        UdpNsEchoEndPoint1 = new IPEndPoint(testIps.LocalTestIps[3], 53);
        HttpV4EndPointRefused1 = AllocateFreeTcpEndPoint(testIps.LocalTestIps[0]);
    }

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

    public IReadOnlyList<IPEndPoint> AllTcpDataEndPoints => [
        TcpDataEndPoint1
    ];

    public IReadOnlyList<IPEndPoint> AllUdpEchoEndPoints => [
        UdpEchoEndPoint1,
        UdpEchoEndPoint2,
        UdpEchoEndPoint3,
        UdpEchoEndPoint1V6,
        UdpEchoEndPoint2V6,
        UdpNsEchoEndPoint1
    ];
}

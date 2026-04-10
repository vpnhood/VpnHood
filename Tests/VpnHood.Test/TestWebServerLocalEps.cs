using System.Net;
using VpnHood.Test.Providers;

namespace VpnHood.Test;

public class TestWebServerLocalEps(TestIps testIps)
{
    public IPEndPoint HttpsV4EndPoint1 => new(testIps.LocalTestIps[0], 15000);
    public IPEndPoint HttpsV4EndPoint2 => new(testIps.LocalTestIps[1], 15001);

    public IPEndPoint HttpV4EndPoint1 => new(testIps.LocalTestIps[0], 15010);
    public IPEndPoint HttpV4EndPoint2 => new(testIps.LocalTestIps[1], 15011);

    public IPEndPoint UdpEchoEndPoint1 => new(testIps.LocalTestIps[0], 20100);
    public IPEndPoint UdpEchoEndPoint2 => new(testIps.LocalTestIps[1], 20101);
    public IPEndPoint UdpEchoEndPoint3 => new(testIps.LocalTestIps[1], 20102);
    public IPEndPoint UdpEchoEndPoint1V6 => new(testIps.LocalTestIpV6, 20201);
    public IPEndPoint UdpEchoEndPoint2V6 => new(testIps.LocalTestIpV6, 20202);

    public IPEndPoint QuicEndPoint1 => new(testIps.LocalTestIps[0], 25001); // must be 127.0.0.1 for quic to work on loopback adapter
    public IPEndPoint QuicEndPoint2 => new(testIps.LocalTestIps[0], 25002); // must be 127.0.0.1 for quic to work on loopback adapter

    public IPEndPoint TcpDataEndPoint1 => new(testIps.LocalTestIps[0], 15020);

    public IPEndPoint HttpV4EndPointBlockedClient => new(testIps.LocalBlockedClientIpAddress, 15009);
    public IPEndPoint HttpV4EndPointBlockedServer => new(testIps.LocalBlockedServerIpAddress, 15010);
    public IPEndPoint UdpNsEchoEndPoint1 => new(testIps.LocalTestIps[3], 53);
    public IPEndPoint HttpV4EndPointRefused1 => new(testIps.LocalTestIps[0], 9999);
    public Uri HttpsUrl1 => new($"https://{HttpsV4EndPoint1}/file1");
    public Uri HttpsUrl2 => new($"https://{HttpsV4EndPoint2}/file1");
    public Uri HttpUrl1 => new($"http://{HttpV4EndPoint1}/file1");
    public Uri HttpUrl2 => new($"http://{HttpV4EndPoint2}/file2");
    public Uri QuicUrl1 => new($"https://{QuicEndPoint1}/file1");
    public Uri QuicUrl2 => new($"https://{QuicEndPoint2}/file2");

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

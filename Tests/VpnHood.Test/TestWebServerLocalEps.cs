using System.Net;
using System.Net.Sockets;
using VpnHood.Test.Providers;

namespace VpnHood.Test;

public class TestWebServerLocalEps
{
    private readonly TestIps _testIps;
    public IPEndPoint HttpsV4EndPoint1 => new(_testIps.LocalTestIps[0], 15000);
    public IPEndPoint HttpsV4EndPoint2 => new(_testIps.LocalTestIps[1], 15001);

    public IPEndPoint HttpV4EndPoint1 => new(_testIps.LocalTestIps[0], 15010);
    public IPEndPoint HttpV4EndPoint2 => new(_testIps.LocalTestIps[1], 15011);

    public IPEndPoint UdpEchoEndPoint1 => new IPEndPoint(_testIps.LocalTestIps[0], 20100);
    public IPEndPoint UdpEchoEndPoint2 => new IPEndPoint(_testIps.LocalTestIps[0], 20101);
    public IPEndPoint UdpEchoEndPoint1V6 => new IPEndPoint(_testIps.LocalTestIpV6, 20201);
    public IPEndPoint UdpEchoEndPoint2V6 => new IPEndPoint(_testIps.LocalTestIpV6, 20202);


    public IPEndPoint[] UdpEndPoints { get; }
    public IPEndPoint[] QuicEndPoints { get; }
    public IPEndPoint HttpV4EndPointBlockedClient => new(_testIps.LocalBlockedClientIpAddress, 15009);
    public IPEndPoint HttpV4EndPointBlockedServer => new(_testIps.LocalBlockedServerIpAddress, 15010);
    public IPEndPoint UdpNsEchoEndPoint1 { get; }
    public IPEndPoint HttpV4EndPointRefused1 => new(_testIps.LocalTestIps[0], 9999);
    public IPEndPoint[] UdpV4EndPoints => UdpEndPoints.Where(x => x.AddressFamily == AddressFamily.InterNetwork).ToArray();
    public IPEndPoint[] UdpV6EndPoints => UdpEndPoints.Where(x => x.AddressFamily == AddressFamily.InterNetworkV6).ToArray();
    public Uri HttpsUrl1 => new($"https://{HttpsV4EndPoint1}/file1");
    public Uri HttpsUrl2 => new($"https://{HttpsV4EndPoint2}/file1");
    public Uri HttpUrl1 => new Uri($"http://{HttpV4EndPoint1}/file1");
    public Uri HttpUrl2 => new Uri($"http://{HttpV4EndPoint2}/file2");

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

    public TestWebServerLocalEps(TestIps testIps)
    {
        _testIps = testIps;

        UdpEndPoints = [
            new IPEndPoint(testIps.LocalTestIps[0], 20100),
            new IPEndPoint(testIps.LocalTestIps[1], 20101),
            new IPEndPoint(testIps.LocalTestIps[2], 20102),
            new IPEndPoint(testIps.LocalTestIpV6, 20101),
            new IPEndPoint(testIps.LocalTestIpV6, 20102),
            new IPEndPoint(testIps.LocalTestIpV6, 20103)
        ];

        UdpNsEchoEndPoint1 = new IPEndPoint(testIps.LocalTestIps[3], 53);

        QuicEndPoints = [
            new IPEndPoint(testIps.LocalTestIps[0], 25001),
            new IPEndPoint(testIps.LocalTestIps[1], 25001),
            new IPEndPoint(testIps.LocalTestIps[2], 25002),
        ];
    }
}

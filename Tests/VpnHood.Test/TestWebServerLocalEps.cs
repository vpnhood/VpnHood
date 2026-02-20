using System.Net;
using System.Net.Sockets;
using VpnHood.Test.Providers;

namespace VpnHood.Test;

public class TestWebServerLocalEps
{
    public IPEndPoint[] HttpsV4EndPoints { get; }
    public IPEndPoint[] HttpV4EndPoints { get; }
    public IPEndPoint[] UdpEndPoints { get; }
    public IPEndPoint[] QuicEndPoints { get; }
    public IPEndPoint HttpV4EndPointBlockedClient { get; }
    public IPEndPoint HttpV4EndPointBlockedServer { get; }
    public IPEndPoint UdpNsEchoEndPoint1 { get; }
    public IPEndPoint HttpV4EndPointRefused => new(HttpV4EndPoints[0].Address, 9999);
    public IPEndPoint[] UdpV4EndPoints => UdpEndPoints.Where(x => x.AddressFamily == AddressFamily.InterNetwork).ToArray();
    public IPEndPoint[] UdpV6EndPoints => UdpEndPoints.Where(x => x.AddressFamily == AddressFamily.InterNetworkV6).ToArray();
    public Uri[] HttpUrls { get; }
    public Uri[] HttpsUrls { get; }
    public Uri FileHttpUrl1 { get; }
    public Uri FileHttpUrl2 { get; }

    public TestWebServerLocalEps(TestIps testIps)
    {
        HttpV4EndPointBlockedClient = new IPEndPoint(testIps.LocalBlockedClientIpAddress, 15009);
        HttpV4EndPointBlockedServer = new IPEndPoint(testIps.LocalBlockedServerIpAddress, 15010);

        HttpsV4EndPoints = [
            new IPEndPoint(testIps.LocalTestIps[0], 15000),
            new IPEndPoint(testIps.LocalTestIps[1], 15001),
            new IPEndPoint(testIps.LocalTestIps[2], 15002),
        ];

        HttpV4EndPoints = [
            new IPEndPoint(testIps.LocalTestIps[0], 15010),
            new IPEndPoint(testIps.LocalTestIps[1], 15011),
            new IPEndPoint(testIps.LocalTestIps[2], 15012),
            HttpV4EndPointBlockedClient, 
            HttpV4EndPointBlockedServer
        ];

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

        HttpUrls = HttpV4EndPoints.Select(x => new Uri($"http://{x}/file1")).ToArray();
        HttpsUrls = HttpsV4EndPoints.Select(x => new Uri($"https://{x}/file1")).ToArray();
        FileHttpUrl1 = new Uri($"http://{HttpV4EndPoints.First()}/file1");
        FileHttpUrl2 = new Uri($"http://{HttpV4EndPoints.First()}/file2");
    }
}

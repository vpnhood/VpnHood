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
    public IPEndPoint[] UdpV4EndPoints => UdpEndPoints.Where(x => x.AddressFamily == AddressFamily.InterNetwork).ToArray();
    public IPEndPoint[] UdpV6EndPoints => UdpEndPoints.Where(x => x.AddressFamily == AddressFamily.InterNetworkV6).ToArray();
    public Uri[] HttpUrls { get; }
    public Uri[] HttpsUrls { get; }
    public Uri FileHttpUrl1 { get; }
    public Uri FileHttpUrl2 { get; }

    public TestWebServerLocalEps(TestIps testIps)
    {
        HttpsV4EndPoints = [
            new IPEndPoint(testIps.LocalTestIpV4, 15001),
            new IPEndPoint(testIps.LocalTestIpV4, 15002),
            new IPEndPoint(testIps.LocalTestIpV4, 15003),
            new IPEndPoint(testIps.LocalTestIpV4, 15004)
        ];

        HttpV4EndPoints = [
            new IPEndPoint(testIps.LocalTestIpV4, 15005),
            new IPEndPoint(testIps.LocalTestIpV4, 15006),
            new IPEndPoint(testIps.LocalTestIpV4, 15007),
            new IPEndPoint(testIps.LocalTestIpV4, 15008)
        ];

        UdpEndPoints = [
            new IPEndPoint(testIps.LocalTestIpV4, 20101),
            new IPEndPoint(testIps.LocalTestIpV4, 20102),
            new IPEndPoint(testIps.LocalTestIpV4, 20103),
            new IPEndPoint(testIps.LocalTestIpV6, 20101),
            new IPEndPoint(testIps.LocalTestIpV6, 20102),
            new IPEndPoint(testIps.LocalTestIpV6, 20103)
        ];

        QuicEndPoints = [
            new IPEndPoint(testIps.LocalTestIpV4, 25001),
            new IPEndPoint(testIps.LocalTestIpV4, 25002)
        ];

        HttpUrls = HttpV4EndPoints.Select(x => new Uri($"http://{x}/file1")).ToArray();
        HttpsUrls = HttpsV4EndPoints.Select(x => new Uri($"https://{x}/file1")).ToArray();
        FileHttpUrl1 = new Uri($"http://{HttpV4EndPoints.First()}/file1");
        FileHttpUrl2 = new Uri($"http://{HttpV4EndPoints.First()}/file2");
    }
}

using System.Net;
using VpnHood.Test.Providers;

namespace VpnHood.Test;

public class TestWebServerMockEps(TestWebServerLocalEps localEps, TestIps testIps)
{
    private IPEndPoint MapToRemote(IPEndPoint localEndPoint)
    {
        return testIps.MapToRemote(localEndPoint);
    }

    private Uri MapToRemote(Uri url)
    {
        return testIps.MapToRemote(url);
    }

    public IPAddress IpInvalid => testIps.RemoteInvalidTestIpV4;
    public IPEndPoint QuicEndPoint1 => MapToRemote(localEps.QuicEndPoints[0]);
    public IPEndPoint QuicEndPoint2 => MapToRemote(localEps.QuicEndPoints[1]);
    public IPEndPoint HttpsV4EndPoint1 => MapToRemote(localEps.HttpsV4EndPoint1);
    public IPEndPoint HttpsV4EndPoint2 => MapToRemote(localEps.HttpsV4EndPoint2);
    public IPEndPoint HttpV4EndPoint1 => MapToRemote(localEps.HttpV4EndPoint1);
    public IPEndPoint HttpV4EndPoint2 => MapToRemote(localEps.HttpV4EndPoint2);
    public IPEndPoint HttpV4EndPointBlockedClient => MapToRemote(localEps.HttpV4EndPointBlockedClient);
    public IPEndPoint HttpV4EndPointBlockedServer => MapToRemote(localEps.HttpV4EndPointBlockedServer);
    public IPEndPoint HttpV4EndPointRefused => MapToRemote(localEps.HttpV4EndPointRefused1);
    public IPEndPoint HttpV4EndPointInvalid => new(IpInvalid, 443);
    public IPEndPoint UdpV4EndPoint1 => MapToRemote(localEps.UdpV4EndPoints[0]);
    public IPEndPoint UdpV4EndPoint2 => MapToRemote(localEps.UdpV4EndPoints[1]);
    public IPEndPoint UdpV6EndPoint1 => MapToRemote(localEps.UdpV6EndPoints[0]);
    public IPEndPoint UdpV6EndPoint2 => MapToRemote(localEps.UdpV6EndPoints[1]);
    public IPEndPoint UdpNsEchoEndPoint1 => MapToRemote(localEps.UdpNsEchoEndPoint1);
    public IPEndPoint HttpsV4RefusedEndPoint1 => MapToRemote(localEps.HttpV4EndPointRefused1);
    public Uri HttpUrlInvalid => new Uri($"https://{HttpV4EndPointInvalid}/foo");
    public Uri HttpsUrl1 => new UriBuilder(localEps.HttpsUrl1) { Host = "test-domain1" }.Uri;
    public Uri HttpsUrl2 => new UriBuilder(localEps.HttpsUrl2) { Host = "foo.test-domain2" }.Uri;
    public Uri HttpUrl1 => MapToRemote(localEps.HttpUrl1);
    public Uri HttpUrl2 => MapToRemote(localEps.HttpUrl2);
    public Uri FileHttpUrl1 => MapToRemote(localEps.HttpUrl1);
    public Uri FileHttpUrl2 => MapToRemote(localEps.HttpUrl2);
    public Uri HttpRefusedUri => new($"https://{HttpV4EndPointRefused}/file2");
    public Uri HttpBlockedServerUri => new($"https://{HttpV4EndPointBlockedServer}/file2");
    public IPAddress PingV4Address1 => testIps.RemoteTestIps[0];
    public IPAddress PingV6Address1 => testIps.RemoteTestIpV6;
}
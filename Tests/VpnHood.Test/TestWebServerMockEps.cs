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

    public IPEndPoint QuicEndPoint1 => MapToRemote(localEps.QuicEndPoints[0]);
    public IPEndPoint QuicEndPoint2 => MapToRemote(localEps.QuicEndPoints[1]);
    public IPEndPoint HttpsV4EndPoint1 => MapToRemote(localEps.HttpsV4EndPoints[0]);
    public IPEndPoint HttpsV4EndPoint2 => MapToRemote(localEps.HttpsV4EndPoints[1]);
    public IPEndPoint HttpV4EndPoint1 => MapToRemote(localEps.HttpV4EndPoints[0]);
    public IPEndPoint HttpV4EndPoint2 => MapToRemote(localEps.HttpV4EndPoints[1]);
    public IPEndPoint UdpV4EndPoint1 => MapToRemote(localEps.UdpV4EndPoints[0]);
    public IPEndPoint UdpV4EndPoint2 => MapToRemote(localEps.UdpV4EndPoints[1]);
    public IPEndPoint UdpV6EndPoint1 => MapToRemote(localEps.UdpV6EndPoints[0]);
    public IPEndPoint UdpV6EndPoint2 => MapToRemote(localEps.UdpV6EndPoints[1]);
    public IPEndPoint HttpsV4RefusedEndPoint1 =>
        MapToRemote(new IPEndPoint(localEps.HttpsV4EndPoints[0].Address, 9999));
    public Uri HttpsUrl1 => MapToRemote(localEps.HttpsUrls[0]);
    public Uri HttpsUrl2 => MapToRemote(localEps.HttpsUrls[1]);
    public Uri HttpUrl1 => MapToRemote(localEps.HttpUrls[0]);
    public Uri HttpUrl2 => MapToRemote(localEps.HttpUrls[1]);
    public Uri FileHttpUrl1 => MapToRemote(localEps.FileHttpUrl1);
    public Uri FileHttpUrl2 => MapToRemote(localEps.FileHttpUrl2);

}
using System.Net;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Test;

//todo: remove endpoints
public static class TestConstants
{
    public static TimeSpan DefaultHttpTimeout => TimeSpan.FromSeconds(3).WhenNoDebugger();
    public static TimeSpan DefaultUdpTimeout => DefaultHttpTimeout;
    public static TimeSpan DefaultPingTimeout => DefaultUdpTimeout;
    public static TimeSpan? DefaultQuicTimeout => DefaultUdpTimeout;

    public static Uri HttpsUri2 => new($"https://{HttpsEndPoint2}/file2");
    public static Uri HttpsExternalUri1 => new("https://www.wireshark.org/"); //make sure always return same ips
    public static Uri HttpsExternalUri2 => new("https://ip4.me/"); //make sure always return same ips
    public static IPEndPoint HttpsEndPoint1 => IPEndPoint.Parse("198.18.0.1:3030");
    public static IPEndPoint HttpsEndPoint2 => IPEndPoint.Parse("198.18.0.2:3030");
    public static Uri InvalidUri => new("https://DBBC5764-D452-468F-8301-4B315507318F.zz");
    public static IPAddress InvalidIp => IPAddress.Parse("198.51.100.1");
    public static IPEndPoint InvalidEp => IPEndPoint.Parse("198.51.100.2:9999");

}
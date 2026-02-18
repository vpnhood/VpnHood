using System.Net;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Test;

//todo: remove
public static class TestConstants
{
    public static TimeSpan DefaultHttpTimeout => TimeSpan.FromSeconds(3).WhenNoDebugger();
    public static TimeSpan DefaultUdpTimeout => DefaultHttpTimeout;
    public static TimeSpan DefaultPingTimeout => DefaultUdpTimeout;
    public static TimeSpan? DefaultQuicTimeout => DefaultUdpTimeout;

    public static Uri HttpsUri1 => new($"https://{HttpsEndPoint1}/file1");
    public static Uri HttpsUri2 => new($"https://{HttpsEndPoint2}/file2");
    public static Uri HttpsRefusedUri => new($"https://{TcpRefusedEndPoint}/file2");
    public static Uri HttpsExternalUri1 => new("https://www.wireshark.org/"); //make sure always return same ips
    public static Uri HttpsExternalUri2 => new("https://ip4.me/"); //make sure always return same ips
    public static IPEndPoint NsEndPoint1 => IPEndPoint.Parse("1.1.1.1:53");
    public static IPEndPoint NsEndPoint2 => IPEndPoint.Parse("1.0.0.1:53");
    public static IPEndPoint TcpRefusedEndPoint => new(TcpEndPoint1.Address, 9999);
    public static IPEndPoint TcpEndPoint1 => IPEndPoint.Parse("198.18.0.1:80");
    public static IPEndPoint TcpEndPoint2 => IPEndPoint.Parse("198.18.0.2:80");
    public static IPEndPoint HttpsEndPoint1 => IPEndPoint.Parse("198.18.0.1:3030");
    public static IPEndPoint HttpsEndPoint2 => IPEndPoint.Parse("198.18.0.2:3030");
    public static IPEndPoint UdpV4EndPoint1 => IPEndPoint.Parse("198.18.10.1:63100");
    public static IPEndPoint UdpV4EndPoint2 => IPEndPoint.Parse("198.18.10.2:63101");
    public static Uri InvalidUri => new("https://DBBC5764-D452-468F-8301-4B315507318F.zz");
    public static IPAddress InvalidIp => IPAddress.Parse("198.51.100.1");
    public static IPEndPoint InvalidEp => IPEndPoint.Parse("198.51.100.2:9999");

}
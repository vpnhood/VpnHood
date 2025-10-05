using System.Net;

namespace VpnHood.AppLib.WebServer;

public class WebServerOptions
{
    public IPAddress ListenAddress { get; init; } = IPAddress.Loopback;
    public bool UseHostName { get; init; }
    public Uri? Url { get; init; }
}

namespace VpnHood.Server;

public class TrackingOptions
{
    public bool LogLocalPort { get; set; }
    public bool LogClientIp { get; set; }

    public bool IsEnabled() => LogClientIp || LogLocalPort;
}
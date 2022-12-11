namespace VpnHood.Server;

public class TrackingOptions
{
    public bool LogClientIp { get; set; }
    public bool LogLocalPort { get; set; }
    public bool LogDestinationIp { get; set; }
    public bool LogDestinationPort { get; set; }
        
    public bool IsEnabled() => LogClientIp || LogLocalPort || LogDestinationIp || LogDestinationPort;
}
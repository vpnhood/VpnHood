namespace VpnHood.Server;

public class TrackingOptions
{
    public bool TrackClientIp { get; set; }
    public bool TrackLocalPort { get; set; }
    public bool TrackDestinationIp { get; set; }
    public bool TrackDestinationPort { get; set; }
        
    public bool IsEnabled() => TrackClientIp || TrackLocalPort || TrackDestinationIp || TrackDestinationPort;
}
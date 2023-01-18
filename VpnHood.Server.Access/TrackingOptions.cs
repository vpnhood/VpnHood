namespace VpnHood.Server;

public class TrackingOptions
{
    public bool TrackClientIp { get; set; }
    public bool TrackLocalPort { get; set; }
    public bool TrackDestinationIp { get; set; }
    public bool TrackDestinationPort { get; set; }
    public bool TrackUdp { get; set; } = true;
    public bool TrackTcp { get; set; } = true;
    public bool TrackIcmp { get; set; } = true;
        
    public bool IsEnabled() => TrackClientIp || TrackLocalPort || TrackDestinationIp || TrackDestinationPort;
}
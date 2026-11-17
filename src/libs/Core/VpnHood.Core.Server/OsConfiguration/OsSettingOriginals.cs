namespace VpnHood.Core.Server.OsConfiguration;

// The OS's own value of each setting the server changed, to put back, beside the value the server set, which tells
// whether the setting still holds it
internal class OsSettingOriginals
{
    public string? TcpCongestionControl { get; set; }
    public string? TcpCongestionControlApplied { get; set; }
    public int? ConnectionTrackingMax { get; set; }
    public int? ConnectionTrackingMaxApplied { get; set; }
    public TimeSpan? ConnectionTrackingTcpIdleTimeout { get; set; }
    public TimeSpan? ConnectionTrackingTcpIdleTimeoutApplied { get; set; }
}

using System.Net;
using VpnHood.Common.Net;

namespace VpnHood.Client.App.Settings;

public class UserSettings
{
    private static readonly ClientOptions DefaultClientOptions = new(); 

    public AppLogSettings Logging { get; set; } = new();
    public string? CultureCode { get; set; }
    public Guid? ClientProfileId { get; set; }
    public string? ServerLocation { get; set; }
    public int MaxDatagramChannelCount { get; set; } = DefaultClientOptions.MaxDatagramChannelCount;
    public bool TunnelClientCountry { get; set; } = true;
    public string[]? AppFilters { get; set; }
    public FilterMode AppFiltersMode { get; set; } = FilterMode.All;
    public bool UseUdpChannel { get; set; } = DefaultClientOptions.UseUdpChannel;
    public bool DropUdpPackets { get; set; } = DefaultClientOptions.DropUdpPackets;
    public bool IncludeLocalNetwork { get; set; } = DefaultClientOptions.IncludeLocalNetwork;
    public IpRange[]? IncludeIpRanges { get; set; } = IpNetwork.All.ToIpRanges().ToArray();
    public IpRange[]? ExcludeIpRanges { get; set; } = IpNetwork.None.ToIpRanges().ToArray();
    public IpRange[]? PacketCaptureIncludeIpRanges { get; set; } = IpNetwork.All.ToIpRanges().ToArray();
    public IpRange[]? PacketCaptureExcludeIpRanges { get; set; } = IpNetwork.None.ToIpRanges().ToArray();
    public bool AllowAnonymousTracker { get; set; } = DefaultClientOptions.AllowAnonymousTracker;
    public IPAddress[]? DnsServers { get; set; }
    public string? DebugData1 { get; set; }
    public string? DebugData2 { get; set; }
}
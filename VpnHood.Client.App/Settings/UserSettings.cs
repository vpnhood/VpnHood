using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;
using VpnHood.Common.Net;
using VpnHood.Tunneling.DomainFiltering;

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
    [JsonConverter(typeof(NullToEmptyArrayConverter<string>))] //todo: remove nullable after migration 4.5.533
    public string[] AppFilters { get; set; } = [];
    public FilterMode AppFiltersMode { get; set; } = FilterMode.All;
    public bool UseUdpChannel { get; set; } = DefaultClientOptions.UseUdpChannel;
    public bool DropUdpPackets { get; set; } = DefaultClientOptions.DropUdpPackets;
    public bool IncludeLocalNetwork { get; set; } = DefaultClientOptions.IncludeLocalNetwork;

    [JsonConverter(typeof(NullToEmptyArrayConverter<IpRange>))] //todo: remove nullable after migration 4.5.533
    public IpRange[] IncludeIpRanges { get; set; } = IpNetwork.All.ToIpRanges().ToArray();

    [JsonConverter(typeof(NullToEmptyArrayConverter<IpRange>))] //todo: remove nullable after migration 4.5.533
    public IpRange[] ExcludeIpRanges { get; set; } = IpNetwork.None.ToIpRanges().ToArray();

    [JsonConverter(typeof(NullToEmptyArrayConverter<IpRange>))] //todo: remove nullable after migration 4.5.533
    public IpRange[] PacketCaptureIncludeIpRanges { get; set; } = IpNetwork.All.ToIpRanges().ToArray();

    [JsonConverter(typeof(NullToEmptyArrayConverter<IpRange>))] //todo: remove nullable after migration 4.5.533
    public IpRange[] PacketCaptureExcludeIpRanges { get; set; } = IpNetwork.None.ToIpRanges().ToArray();
    public bool AllowAnonymousTracker { get; set; } = DefaultClientOptions.AllowAnonymousTracker;
    public IPAddress[]? DnsServers { get; set; }
    public DomainFilter DomainFilter { get; set; } = new();
    public string? DebugData1 { get; set; }
    public string? DebugData2 { get; set; }
}
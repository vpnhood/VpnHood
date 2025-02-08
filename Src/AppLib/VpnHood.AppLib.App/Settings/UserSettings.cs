using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Converters;

namespace VpnHood.AppLib.Settings;

public class UserSettings
{
    private static readonly ClientOptions DefaultClientOptions = new();
    public bool IsLicenseAccepted { get; set; }
    public string? CultureCode { get; set; }
    public Guid? ClientProfileId { get; set; }
    public int MaxDatagramChannelCount { get; set; } = DefaultClientOptions.MaxDatagramChannelCount;
    public bool TunnelClientCountry { get; set; } = true;

    [JsonConverter(typeof(NullToEmptyArrayConverter<string>))] //todo: remove nullable after migration 4.5.533
    public string[] AppFilters { get; set; } = [];

    public FilterMode AppFiltersMode { get; set; } = FilterMode.All;
    public bool UseUdpChannel { get; set; } = DefaultClientOptions.UseUdpChannel;
    public bool DropUdp { get; set; } = DefaultClientOptions.DropUdp;
    public bool DropQuic { get; set; } = DefaultClientOptions.DropQuic;
    public bool AllowAnonymousTracker { get; set; } = DefaultClientOptions.AllowAnonymousTracker;
    public IPAddress[]? DnsServers { get; set; }
    public DomainFilter DomainFilter { get; set; } = new();
    public string? DebugData1 { get; set; }
    public string? DebugData2 { get; set; }
    public bool LogAnonymous { get; set; } = true;
    public bool IncludeLocalNetwork { get; set; } = DefaultClientOptions.IncludeLocalNetwork;
    public bool UseAppIpFilter { get; set; }
    public bool UseVpnAdapterIpFilter { get; set; }
}
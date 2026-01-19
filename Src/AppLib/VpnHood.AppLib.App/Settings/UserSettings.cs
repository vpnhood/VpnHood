using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Toolkit.Converters;

namespace VpnHood.AppLib.Settings;

public class UserSettings
{
    public bool IsLicenseAccepted { get; set; }
    public bool IsTcpProxyPrompted { get; set; }
    public bool IsQuickLaunchPrompted { get; set; }
    public string? CultureCode { get; set; }
    public Guid? ClientProfileId { get; set; }
    public int MaxPacketChannelCount { get; set; } = ClientOptions.Default.MaxPacketChannelCount;
    public CountryFilterMode CountryFilterMode { get; set; } = CountryFilterMode.IncludeAll;
    public string[] CountryFilters { get; set; } = [];
    public string[] AppFilters { get; set; } = [];
    public FilterMode AppFiltersMode { get; set; } = FilterMode.All;
    public ChannelProtocol ChannelProtocol { get; set; } = ChannelProtocol.Tcp;
    public bool DropUdp { get; set; } = ClientOptions.Default.DropUdp;
    public bool UseTcpProxy { get; set; }
    public bool DropQuic { get; set; }
    public bool AllowAnonymousTracker { get; set; } = ClientOptions.Default.AllowAnonymousTracker;
    public DomainFilter DomainFilter { get; set; } = new();
    public string? DebugData1 { get; set; }
    public string? DebugData2 { get; set; }
    public bool LogAnonymous { get; set; } = true;
    public bool IncludeLocalNetwork { get; set; } = ClientOptions.Default.IncludeLocalNetwork;
    public bool UseAppIpFilter { get; set; }
    public bool UseVpnAdapterIpFilter { get; set; }
    public EndPointStrategy EndPointStrategy { get; set; }
    public DnsMode DnsMode { get; set; }
    public AppProxySettings ProxySettings { get; set; } = new();
    public bool AllowRemoteAccess { get; set; }
    public JsonElement? CustomData { get; set; }


    // for compatibility convert old nullable to empty array
    [JsonConverter(typeof(ArrayConverter<IPAddress, IPAddressConverter>))]
    public IPAddress[] DnsServers {
        get;
        // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
        set => field = value ?? [];
    } = [];

    [Obsolete("Compatibility for version <= 759; Use ChannelProtocol.")]
    public bool? UseUdpChannel {
        // ReSharper disable once ValueParameterNotUsed
        init {
            if (value == true)
                ChannelProtocol = ChannelProtocol.Udp;
        }
    }

    [Obsolete("Use CountryFilterMode.")]
    public bool TunnelClientCountry {
        init {
            if (!value)
                CountryFilterMode = CountryFilterMode.ExcludeMyCountry;
            else
                CountryFilterMode = CountryFilterMode.IncludeAll; //todo: remove
        }
        get => CountryFilterMode != CountryFilterMode.ExcludeMyCountry; //todo: remove
    }
}
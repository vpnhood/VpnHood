using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using VpnHood.AppLib.Api.Sessions;
using VpnHood.Core.Toolkit.Converters;

namespace VpnHood.AppLib.Api.Settings;

public class UserSettings
{
    public bool IsLicenseAccepted { get; set; }
    public bool IsTcpProxyPrompted { get; set; }
    public bool IsQuickLaunchPrompted { get; set; }
    public string? CultureCode { get; set; }
    public string? CountryCode { get; set; }
    public Guid? ClientProfileId { get; set; }
    public SplitTunnelingSettings SplitTunneling { get; set; } = new();
    public ChannelProtocol ChannelProtocol { get; set; } = ChannelProtocol.Tcp;
    public bool DropUdp { get; set; }
    public bool UseTcpProxy { get; set; }
    public bool DropQuic { get; set; }
    // the engine's own default for this, stated rather than borrowed: the contract does not
    // reach into ClientOptions to find out what it should say
    public bool AllowAnonymousTracker { get; set; } = true;
    public string? DebugData1 { get; set; }
    public string? DebugData2 { get; set; }
    public bool LogAnonymous { get; set; } = true;
    public EndPointStrategy EndPointStrategy { get; set; }
    public DnsMode DnsMode { get; set; }
    public AppProxySettings ProxySettings { get; set; } = new();
    public JsonElement? CustomData { get; set; }


    // for compatibility convert old nullable to empty array
    [JsonConverter(typeof(ArrayConverter<IPAddress, IPAddressConverter>))]
    public IPAddress[] DnsServers {
        get;
        // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
        set => field = value ?? [];
    } = [];
}
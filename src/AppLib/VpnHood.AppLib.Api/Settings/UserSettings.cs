using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using VpnHood.AppLib.Api.Sessions;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.AppLib.Api.Settings;

public class UserSettings
{
    public bool IsLicenseAccepted { get; set; }
    public bool IsTcpProxyPrompted { get; set; }
    public bool IsQuickLaunchPrompted { get; set; }
    public string? CultureCode { get; set; }
    public string? CountryCode { get; set; }
    public Guid? VpnProfileId { get; set; }

    // Where the app keeps always on itself (AlwaysOnSupport.App): the service connects when it starts,
    // before anyone signs in, and again after a failure.
    public bool AlwaysOn { get; set; }

    // Where the device has a kill switch (IDevice.KillSwitch): nothing leaves outside the tunnel from a
    // connect until a person disconnects, through an error stop, a crash and a reboot.
    public bool KillSwitch { get; set; }

    // With the kill switch on, its block stays after a person's disconnect too, by these settings' split, and goes
    // up at once when this is turned on while disconnected; only turning this or the switch off lifts it. On by
    // default; a hotel's Wi-Fi sign-in page needs it off first.
    public bool KillSwitchLockdown { get; set; } = true;
    public SplitTunnelingSettings SplitTunneling { get; set; } = new();
    public ChannelProtocol ChannelProtocol { get; set; } = ChannelProtocol.Tcp;
    public bool DropUdp { get; set; }
    public bool UseTcpProxy { get; set; }
    public bool DropQuic { get; set; }
    // the engine's own default for this, stated rather than borrowed: the contract does not
    // reach into ClientOptions to find out what it should say
    public bool AllowAnonymousTracker { get; set; } = true;
    // the person answered the usage-data question, which is asked where the law wants a yes first
    // (AppTrackerService); until then the switch's default counts for nothing there
    public bool IsAnonymousTrackerPrompted { get; set; }
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
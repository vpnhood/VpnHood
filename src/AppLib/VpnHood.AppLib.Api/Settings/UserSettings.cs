using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using VpnHood.AppLib.Api.Sessions;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.AppLib.Api.Settings;

/// <summary>Represents the person's settings, which the app saves.</summary>
public class UserSettings
{
    /// <summary>Gets or sets a value indicating whether the person has accepted the license agreement.</summary>
    public bool IsLicenseAccepted { get; set; }

    /// <summary>Gets or sets a value indicating whether the app has asked the person about the TCP forwarding.</summary>
    public bool IsTcpForwardingPrompted { get; set; }

    /// <summary>Gets or sets a value indicating whether the app has offered the person the Quick Launch.</summary>
    public bool IsQuickLaunchPrompted { get; set; }

    /// <summary>Gets or sets the language the person chose for the UI, by its culture name, or <see langword="null"/> for the system's.</summary>
    public string? CultureCode { get; set; }

    /// <summary>Gets or sets the client's country as the server last reported it, which the app takes as its region, or <see langword="null"/> for the device's.</summary>
    public string? CountryCode { get; set; }

    /// <summary>Gets or sets the current VPN profile's ID, or <see langword="null"/>.</summary>
    public Guid? VpnProfileId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the app keeps the VPN on at all times, where it keeps always-on itself
    /// (<see cref="VpnHood.AppLib.Api.App.AlwaysOnSupport.App"/>): the service connects when it starts, before anyone signs in,
    /// and again after a failure.
    /// </summary>
    public bool AlwaysOn { get; set; }

    /// <summary>Gets or sets the kill switch's level, where the app keeps a kill switch of its own (<see cref="VpnHood.AppLib.Api.App.AppDescriptor.IsKillSwitchSupported"/>).</summary>
    public KillSwitchMode KillSwitch { get; set; }

    /// <summary>Gets or sets the split tunneling's settings.</summary>
    public SplitTunnelingSettings SplitTunneling { get; set; } = new();

    /// <summary>Gets or sets the protocol of the tunnel's packet channels. The default is <see cref="ChannelProtocol.Tcp"/>.</summary>
    public ChannelProtocol ChannelProtocol { get; set; } = ChannelProtocol.Tcp;

    /// <summary>
    /// Gets or sets a value indicating whether the client drops the UDP bound for the tunnel, but to DNS's ports; it
    /// applies only while the client forwards IPv4 TCP, and then to either IP version.
    /// </summary>
    public bool DropUdp { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the client ends each TCP connection on the device and has the server
    /// open a new one, rather than carrying its packets.
    /// </summary>
    /// <remarks>
    /// It is a preference, for each IP version: a server that cannot forward TCP turns it off, and one that does not
    /// carry that version's TCP as packets, or a domain filter, which needs it, turns it on.
    /// </remarks>
    public bool UseTcpForwarding { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the client drops the QUIC bound for the tunnel, UDP to ports 80 and 443,
    /// so apps turn to TCP; it applies only while the client forwards IPv4 TCP, and then to either IP version.
    /// </summary>
    public bool DropQuic { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the person lets their use be reported anonymously, by the app and through
    /// the VPN's session. The default is <see langword="true"/>.
    /// </summary>
    public bool AllowAnonymousTracker { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the person has answered the usage-data question, which is asked where the law wants a yes first; until then, the switch's default counts for nothing there.</summary>
    public bool IsAnonymousTrackerPrompted { get; set; }

    /// <summary>
    /// Gets or sets text of debug switches the developer gave, such as <c>/disable-WebSocket</c>, or
    /// <see langword="null"/>.
    /// </summary>
    public string? DebugData1 { get; set; }

    /// <summary>Gets or sets a second text of debug switches the developer gave, or <see langword="null"/>.</summary>
    public string? DebugData2 { get; set; }

    /// <summary>Gets or sets a value indicating whether the log redacts identifying values: addresses, endpoints, networks, IDs and host names. The default is <see langword="true"/>; a debug build logs them, and the app's own options may require the redaction.</summary>
    public bool LogAnonymous { get; set; } = true;

    /// <summary>
    /// Gets or sets how the client orders the server token's addresses. The default is
    /// <see cref="EndPointStrategy.Auto"/>, the token's own strategy.
    /// </summary>
    public EndPointStrategy EndPointStrategy { get; set; }

    /// <summary>Gets or sets which DNS servers the VPN uses.</summary>
    public DnsMode DnsMode { get; set; }

    /// <summary>Gets or sets the proxy settings.</summary>
    public AppProxySettings ProxySettings { get; set; } = new();

    /// <summary>Gets or sets JSON a UI keeps of its own, which the app does not read, or <see langword="null"/>.</summary>
    public JsonElement? CustomData { get; set; }

    // for compatibility convert old nullable to empty array

    /// <summary>
    /// Gets or sets the person's own DNS servers, which <see cref="DnsMode.AdapterDns"/> uses; none by default, and a
    /// <see langword="null"/> one reads as none.
    /// </summary>
    [JsonConverter(typeof(ArrayConverter<IPAddress, IPAddressConverter>))]
    public IPAddress[] DnsServers {
        get;
        // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
        set => field = value ?? [];
    } = [];
}
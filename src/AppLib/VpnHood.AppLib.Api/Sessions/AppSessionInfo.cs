using System.Net;
using System.Text.Json.Serialization;
using VpnHood.AppLib.Api.VpnProfiles;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.AppLib.Api.Sessions;

/// <summary>
/// Represents a VPN session as its server set it up.
/// </summary>
public class AppSessionInfo
{
    /// <summary>Gets the access's terms and state, as its access manager reports them, or <see langword="null"/>.</summary>
    public required AccessInfo? AccessInfo { get; init; }

    /// <summary>Gets the session's DNS: its servers, where they came from, and whether the tunnel carries them.</summary>
    public required DnsConfig DnsConfig { get; init; }

    /// <summary>Gets or sets a value indicating whether the server lets the client reach the local networks.</summary>
    public required bool IsLocalNetworkAllowed { get; set; }

    /// <summary>
    /// Gets a value indicating whether the server's own configuration leaves public destinations outside the tunnel.
    /// </summary>
    /// <remarks>
    /// The local networks, multicast and loopback the server leaves out do not count, as they cannot expose the public
    /// address; nor does an IP version the server carries not at all, which <see cref="IsIpV6SupportedByServer"/>
    /// reports.
    /// </remarks>
    public required bool IsTrafficSplitByServer { get; init; }

    /// <summary>
    /// Gets a value indicating whether the server carries IPv6; when not, the client blocks or bypasses IPv6 as
    /// the split tunneling's settings say.
    /// </summary>
    public required bool IsIpV6SupportedByServer { get; init; }

    /// <summary>Gets the server's location, or <see langword="null"/>.</summary>
    public required CurrentServerLocationInfo? ServerLocationInfo { get; init; }

    /// <summary>Gets a value indicating whether the session is premium.</summary>
    public required bool IsPremiumSession { get; init; }

    /// <summary>Gets whose session this one took the place of, if any.</summary>
    public required SessionSuppressType SuppressedTo { get; init; }

    /// <summary>Gets the server's version.</summary>
    [JsonConverter(typeof(VersionConverter))]
    public required Version ServerVersion { get; init; }

    /// <summary>Gets the client's public IP address as the server sees it.</summary>
    [JsonConverter(typeof(IPAddressConverter))]
    public required IPAddress ClientPublicIpAddress { get; init; }

    /// <summary>Gets or sets when the session was made, in UTC.</summary>
    public required DateTime CreatedTime { get; init; }

    /// <summary>
    /// Gets the protocols the session's packet channels can use, by what the server takes and this device can make.
    /// </summary>
    public required IReadOnlyList<ChannelProtocol> ChannelProtocols { get; init; }

    /// <summary>Gets a value indicating whether the server forwards TCP connections the client ends on the device.</summary>
    public required bool IsTcpForwardingSupported { get; init; }

    /// <summary>
    /// Gets a value indicating whether the server takes TCP as packets over IPv4, which the TCP forwarding switch needs.
    /// </summary>
    public required bool IsTcpPacketSupported { get; init; }

    /// <summary>
    /// Gets a value indicating whether the server carries the device's QUIC; where it does not, the client drops it as
    /// <see cref="VpnHood.AppLib.Api.Settings.UserSettings.DropQuic"/> does, whatever that says, while it forwards IPv4 TCP.
    /// </summary>
    public required bool IsQuicPacketSupported { get; init; }

    /// <summary>
    /// Gets a value indicating whether the server carries the device's UDP; where it does not, the client drops it as
    /// <see cref="VpnHood.AppLib.Api.Settings.UserSettings.DropUdp"/> does, whatever that says, while it forwards IPv4 TCP.
    /// </summary>
    public required bool IsUdpPacketSupported { get; init; }
}
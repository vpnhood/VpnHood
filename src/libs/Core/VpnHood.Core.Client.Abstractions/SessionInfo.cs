using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Core.Client.Abstractions;

/// <summary>
/// Represents a VPN session as its server set it up.
/// </summary>
public class SessionInfo
{
    /// <summary>Gets the session's ID.</summary>
    public required string SessionId { get; init; }

    /// <summary>Gets the access's terms and state, as its access manager reports them, or <see langword="null"/>.</summary>
    public required AccessInfo? AccessInfo { get; init; }

    /// <summary>Gets or sets when the session was made, in UTC.</summary>
    public required DateTime CreatedTime { get; set; }

    /// <summary>Gets a value indicating whether the server takes UDP packet channels.</summary>
    public required bool IsUdpChannelSupported { get; init; }

    /// <summary>
    /// Gets a value indicating whether the session can use QUIC packet channels: the server takes them, and this device
    /// can make QUIC connections.
    /// </summary>
    public required bool IsQuicChannelSupported { get; init; }

    /// <summary>Gets a value indicating whether the server carries IPv4 TCP as packets.</summary>
    public required bool IsTcpPacketIpV4Supported { get; init; }

    /// <summary>Gets a value indicating whether the server carries IPv6 TCP as packets.</summary>
    public required bool IsTcpPacketIpV6Supported { get; init; }

    /// <summary>Gets a value indicating whether the server forwards TCP connections the client ends on the device.</summary>
    public required bool IsTcpForwardingSupported { get; init; }

    /// <summary>
    /// Gets a value indicating whether the server's settings require TCP forwarding, its Cloak Mode, which turns on
    /// <see cref="ClientOptions.DropQuic"/> and <see cref="ClientOptions.DropUdp"/> as the user's TCP forwarding does. An
    /// older server, which does not say, is taken to be in it where it takes no TCP as packets at all.
    /// </summary>
    public bool IsTcpForwardingRequired { get; init; }

    /// <summary>
    /// Gets a value indicating whether the server carries the device's QUIC; where it does not, the client drops it as
    /// <see cref="ClientOptions.DropQuic"/> does, whatever that says.
    /// </summary>
    public required bool IsQuicPacketSupported { get; init; }

    /// <summary>
    /// Gets a value indicating whether the server carries the device's UDP; where it does not, the client drops it as
    /// <see cref="ClientOptions.DropUdp"/> does, whatever that says.
    /// </summary>
    public required bool IsUdpPacketSupported { get; init; }

    /// <summary>Gets or sets a value indicating whether the server lets the client reach the local networks.</summary>
    public required bool IsLocalNetworkAllowed { get; set; }

    /// <summary>Gets the server's location, or <see langword="null"/>.</summary>
    public required ServerLocationInfo? ServerLocationInfo { get; init; }

    /// <summary>Gets a value indicating whether the session is premium.</summary>
    public required bool IsPremiumSession { get; init; }

    /// <summary>Gets whose session this one took the place of, if any.</summary>
    public required SessionSuppressType SuppressedTo { get; init; }

    /// <summary>Gets the ad the session requires.</summary>
    public required AdRequirement AdRequirement { get; init; }

    /// <summary>
    /// Gets the protocols the session's packet channels can use, by what the server takes and this device can make.
    /// </summary>
    public required IReadOnlyList<ChannelProtocol> ChannelProtocols { get; init; }

    /// <summary>
    /// Gets an access key the server gave to replace the client's, or <see langword="null"/>; the app updates its
    /// profile with it after the connect.
    /// </summary>
    public required string? AccessKey { get; init; }

    /// <summary>Gets the code of the country the server places the client in, or <see langword="null"/>.</summary>
    public required string? ClientCountry { get; init; }

    /// <summary>Gets the session's DNS: its servers, where they came from, and whether the tunnel carries them.</summary>
    public required DnsConfig DnsConfig { get; init; }

    /// <summary>
    /// Gets a value indicating whether the server's own configuration leaves public destinations outside the tunnel.
    /// </summary>
    /// <remarks>
    /// Only public destinations count (<see cref="VpnHood.Net.Toolkit.Net.IpNetwork.PublicNetworks"/>): the local
    /// networks, multicast, loopback and other networks that are not public, which the server leaves out, cannot expose
    /// the public address; nor does an IP version the server carries not at all, which
    /// <see cref="IsIpV6SupportedByServer"/> reports.
    /// </remarks>
    public required bool IsTrafficSplitByServer { get; init; }

    /// <summary>
    /// Gets a value indicating whether the server carries IPv6; when not, the client blocks or bypasses IPv6 as
    /// <see cref="ClientOptions.UnsupportedIpV6Mode"/> says.
    /// </summary>
    public required bool IsIpV6SupportedByServer { get; init; }

    /// <summary>Gets the server's version.</summary>
    [JsonConverter(typeof(VersionConverter))]
    public required Version ServerVersion { get; init; }

    /// <summary>Gets the client's public IP address as the server sees it.</summary>
    [JsonConverter(typeof(IPAddressConverter))]
    public required IPAddress ClientPublicIpAddress { get; init; }
}
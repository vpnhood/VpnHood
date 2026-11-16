using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Core.Common.Messaging;
using VpnHood.Net.Toolkit.Converters;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Core.Common.Configuration;

namespace VpnHood.Core.Tunneling.Messaging;

/// <summary>
/// Represents the server's answer to a <see cref="HelloRequest"/>: the session, and what the client needs to run it.
/// </summary>
public class HelloResponse : SessionResponse
{
    /// <summary>Gets or sets the DNS servers the server offers, or <see langword="null"/> for none.</summary>
    [JsonConverter(typeof(ArrayConverter<IPAddress, IPAddressConverter>))]
    public IPAddress[]? DnsServers { get; set; }

    /// <summary>
    /// Gets the client's virtual IPv4 address in the session, with the virtual network's prefix length, or
    /// <see langword="null"/>.
    /// </summary>
    [JsonConverter(typeof(IpNetworkConverter))]
    public IpNetwork? VirtualIpNetworkV4 { get; init; }

    /// <summary>
    /// Gets the client's virtual IPv6 address in the session, with the virtual network's prefix length, or
    /// <see langword="null"/>.
    /// </summary>
    [JsonConverter(typeof(IpNetworkConverter))]
    public IpNetwork? VirtualIpNetworkV6 { get; init; }

    /// <summary>
    /// Gets or sets the server's port for the session's UDP channel, on the address the client came to, or
    /// <see langword="null"/> where it offers none, as to a client below the UDP channel's protocol or through a reverse
    /// proxy.
    /// </summary>
    public int? UdpPort { get; set; }

    /// <summary>
    /// Gets or sets the server's QUIC port, on the address the client came to, or <see langword="null"/> where it offers
    /// none, as to a client on the older HTTP protocol or through a reverse proxy.
    /// </summary>
    public int? QuicPort { get; set; }

    /// <summary>Gets or sets the server's version, as major.minor.build.</summary>
    public string ServerVersion { get; set; } = null!;

    /// <summary>Gets or sets the protocol version the session runs on.</summary>
    public int ProtocolVersion { get; set; }

    /// <summary>
    /// Gets or sets the server farm's secret, for a client below HTTP protocol 15, as older clients had it from here, or
    /// <see langword="null"/>, which is not written.
    /// </summary>
    // Compatibility (2026-10): only pre-v15 clients obtain this from hello; v15 uses its access key.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public byte[]? ServerSecret { get; set; }

    /// <summary>Gets or sets the session's ID.</summary>
    public ulong SessionId { get; set; }

    /// <summary>
    /// Gets or sets the session's key, which the client's requests in the session carry. The default is empty.
    /// </summary>
    public byte[] SessionKey { get; set; } = [];

    /// <summary>Gets or sets whose session this one took the place of, if any.</summary>
    public SessionSuppressType SuppressedTo { get; set; }

    /// <summary>
    /// Gets or sets the most packet channels the session may have at once, or 0 where the server sets no limit.
    /// </summary>
    public int MaxPacketChannelCount { get; set; }

    /// <summary>Gets or sets a value indicating whether the server carries IPv6.</summary>
    public bool IsIpV6Supported { get; set; }

    /// <summary>
    /// Gets or sets the only destinations the server routes, or <see langword="null"/> for all of them.
    /// </summary>
    public IpRange[]? IncludeIpRanges { get; set; }

    /// <summary>
    /// Gets or sets the destinations the server asks the clients' VPN adapters to capture, or <see langword="null"/> for
    /// all of them; the client weighs them with its own settings, and its DNS plan may add to them.
    /// </summary>
    public IpRange[]? VpnAdapterIncludeIpRanges { get; set; }

    /// <summary>
    /// Gets the GA4 measurement ID the client reports the session to, where its person allows it, or
    /// <see langword="null"/>.
    /// </summary>
    public string? GaMeasurementId { get; init; }

    /// <summary>
    /// Gets how long the client waits for an answer to a request. The default is 60 seconds.
    /// </summary>
    /// <remarks>The server sends a little more than its own wait, so its error answers reach the client.</remarks>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets how long a connection the client keeps for reuse may stay idle before it closes. The default is 60 seconds.
    /// </summary>
    /// <remarks>
    /// The server sends a little less than its own, so the client is less likely to reuse a connection the server is
    /// closing.
    /// </remarks>
    public TimeSpan ChannelIdleTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Gets or sets the ad the session requires. The default is none.</summary>
    public AdRequirement AdRequirement { get; set; } = AdRequirement.None;

    /// <summary>Gets or sets the server's location, or <see langword="null"/>.</summary>
    public string? ServerLocation { get; set; }

    /// <summary>Gets or sets the server's tags. The default is none.</summary>
    public string[] ServerTags { get; set; } = [];

    /// <summary>Gets or sets the access's terms and state, or <see langword="null"/>.</summary>
    public AccessInfo? AccessInfo { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the session forwards TCP connections
    /// (<see cref="TcpForwardingChannelRequest"/>). The default is <see langword="true"/>.
    /// </summary>
    public bool IsTcpForwardingSupported { get; set; } = true;

    /// <summary>
    /// Gets or sets <see cref="IsTcpForwardingSupported"/> under its older name, which older clients read and older
    /// servers send.
    /// </summary>
    /// <remarks>It is <see langword="true"/> when missing, so a client without it would try what the server refuses.</remarks>
    // Compatibility (2026-11): drop once MinClientProtocolVersion is above 15 and the servers from before it are gone
    [Obsolete("Use IsTcpForwardingSupported.")]
    public bool IsTcpProxySupported {
        get => IsTcpForwardingSupported;
        set => IsTcpForwardingSupported = value;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the client pads the start of its streams that forward TCP connections,
    /// the WebSocket ones masked as the standard has it; an older server sends none, read as <see langword="false"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool StartPadding { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the session takes IPv4 TCP as packets; where it does not, the client
    /// forwards IPv4 TCP connections instead.
    /// </summary>
    public bool IsTcpPacketIpV4Supported { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the session takes IPv6 TCP as packets; where it does not, the client
    /// forwards IPv6 TCP connections instead.
    /// </summary>
    public bool IsTcpPacketIpV6Supported { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the server carries the users' QUIC; a client drops what it does not, so
    /// it never crosses the link. The default is <see langword="true"/>, as an older server sends none.
    /// </summary>
    public bool IsQuicPacketSupported { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the server carries the users' UDP; a client drops what it does not, so it
    /// never crosses the link. The default is <see langword="true"/>, as an older server sends none.
    /// </summary>
    public bool IsUdpPacketSupported { get; set; } = true;

    /// <summary>
    /// Gets or sets the server's MTU for a client before <see cref="TunnelMtu"/>, which takes the smaller of it and its
    /// own, less <see cref="TransportDefaults.MtuOverhead"/>, as its adapter's. The default is
    /// <see cref="TransportDefaults.MtuServer"/>.
    /// </summary>
    // Compatibility (2026-11): drop once MinClientProtocolVersion is above 15
    public int Mtu { get; set; } = TransportDefaults.MtuServer;

    /// <summary>
    /// Gets or sets the largest packet the session's tunnel carries both ways, as the packet is, or
    /// <see langword="null"/> from an older server.
    /// </summary>
    public int? TunnelMtu { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the session takes TCP as packets, for older clients and from older
    /// servers, where it means both IP versions.
    /// </summary>
    [Obsolete("Deprecated on 2026-10-08: use IsTcpPacketIpV4Supported and IsTcpPacketIpV6Supported. Still " +
              "sent for older clients and read from older servers, for which it meant both IP versions.")]
    public bool IsTcpPacketSupported { get; set; }
}

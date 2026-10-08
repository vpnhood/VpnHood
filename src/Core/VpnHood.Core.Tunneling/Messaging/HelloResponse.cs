using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Core.Common.Messaging;
using VpnHood.Net.Toolkit.Converters;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Core.Common.Configuration;

namespace VpnHood.Core.Tunneling.Messaging;

public class HelloResponse : SessionResponse
{
    [JsonConverter(typeof(ArrayConverter<IPAddress, IPAddressConverter>))]
    public IPAddress[]? DnsServers { get; set; }

    [JsonConverter(typeof(IpNetworkConverter))]
    public IpNetwork? VirtualIpNetworkV4 { get; init; }

    [JsonConverter(typeof(IpNetworkConverter))]
    public IpNetwork? VirtualIpNetworkV6 { get; init; }

    public int? UdpPort { get; set; }
    public int? QuicPort { get; set; }
    public string ServerVersion { get; set; } = null!;
    public int ProtocolVersion { get; set; }
    public byte[] ServerSecret { get; set; } = null!;
    public ulong SessionId { get; set; }
    public byte[] SessionKey { get; set; } = [];
    public SessionSuppressType SuppressedTo { get; set; }
    public int MaxPacketChannelCount { get; set; }
    public bool IsIpV6Supported { get; set; }
    public IpRange[]? IncludeIpRanges { get; set; }
    public IpRange[]? VpnAdapterIncludeIpRanges { get; set; }
    public string? GaMeasurementId { get; init; }
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan ChannelIdleTimeout { get; init; } = TimeSpan.FromSeconds(60);
    public AdRequirement AdRequirement { get; set; } = AdRequirement.None;
    public string? ServerLocation { get; set; }
    public string[] ServerTags { get; set; } = [];
    public AccessInfo? AccessInfo { get; set; }
    public bool IsTcpProxySupported { get; set; } = true;
    public bool IsTcpPacketIpV4Supported { get; set; }
    public bool IsTcpPacketIpV6Supported { get; set; }
    public int Mtu { get; set; } = TransportDefaults.MtuServer;

    [Obsolete("Deprecated on 2026-10-08: use IsTcpPacketIpV4Supported and IsTcpPacketIpV6Supported. Still " +
              "sent for older clients and read from older servers, for which it meant both IP versions.")]
    public bool IsTcpPacketSupported { get; set; }
}
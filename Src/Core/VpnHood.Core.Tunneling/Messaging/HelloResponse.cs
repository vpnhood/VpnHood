using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Toolkit.Converters;
using VpnHood.Core.Toolkit.Net;

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
    public string ServerVersion { get; set; } = null!;
    public int ProtocolVersion { get; set; }
    public byte[] ServerSecret { get; set; } = null!;
    public ulong SessionId { get; set; }
    public byte[] SessionKey { get; set; } = [];
    public SessionSuppressType SuppressedTo { get; set; }
    public int MaxPacketChannelCount { get; set; }
    public bool IsIpV6Supported { get; set; }
    public bool IsReviewRequested { get; set; }
    public IpRange[]? IncludeIpRanges { get; set; }
    public IpRange[]? VpnAdapterIncludeIpRanges { get; set; }
    public string? GaMeasurementId { get; init; }
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan TcpReuseTimeout { get; init; } = TimeSpan.FromSeconds(60);
    public AdRequirement AdRequirement { get; set; } = AdRequirement.None;
    public string? ServerLocation { get; set; }
    public string[] ServerTags { get; set; } = [];
    public AccessInfo? AccessInfo { get; set; }
    public bool IsTunProviderSupported { get; set; }
    public int Mtu { get; set; } = TunnelDefaults.MaxPacketSize;
}
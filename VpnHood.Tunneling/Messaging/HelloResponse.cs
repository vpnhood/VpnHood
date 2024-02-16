using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;

namespace VpnHood.Tunneling.Messaging;

public class HelloResponse : SessionResponse
{
    [JsonConstructor]
    public HelloResponse(SessionErrorCode errorCode)
        : base(errorCode)
    {
    }

    public HelloResponse(SessionResponse obj)
        : base(obj)
    {
    }

    [JsonConverter(typeof(IPAddressConverter))]
    public required IPAddress ClientPublicAddress { get; set; }
    public int? UdpPort { get; set; }
    public string ServerVersion { get; set; } = default!;
    public int ServerProtocolVersion { get; set; }
    public byte[] ServerSecret { get; set; } = default!;
    public ulong SessionId { get; set; }
    public byte[] SessionKey { get; set; } = Array.Empty<byte>();
    public SessionSuppressType SuppressedTo { get; set; } 
    public int MaxDatagramChannelCount { get; set; } 
    public bool IsIpV6Supported { get; set; }
    public IpRange[]? IncludeIpRanges { get; set; }
    public IpRange[]? PacketCaptureIncludeIpRanges { get; set; }
    public string? GaMeasurementId { get; init;}
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan TcpReuseTimeout { get; init; } = TimeSpan.FromSeconds(60);
    public string? AccessKey { get; set; }
}
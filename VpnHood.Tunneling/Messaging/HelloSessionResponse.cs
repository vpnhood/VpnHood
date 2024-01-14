using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;

namespace VpnHood.Tunneling.Messaging;

public class HelloSessionResponse : SessionResponse
{
    [JsonConstructor]
    public HelloSessionResponse(SessionErrorCode errorCode)
        : base(errorCode)
    {
    }

    public HelloSessionResponse(SessionResponse obj)
        : base(obj)
    {
    }

    [JsonConverter(typeof(IPAddressConverter))]
    public IPAddress ClientPublicAddress { get; set; } = default!;

    [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
    public IPEndPoint[] TcpEndPoints { get; set; } = Array.Empty<IPEndPoint>();

    [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
    public IPEndPoint[] UdpEndPoints { get; set; } = Array.Empty<IPEndPoint>();

    public string ServerVersion { get; set; } = default!;
    public int ServerProtocolVersion { get; set; }
    
    public byte[] ServerSecret { get; set; } = default!;
    public int MaxDatagramChannelCount { get; set; } 
    public bool IsIpV6Supported { get; set; }
    public IpRange[]? IncludeIpRanges { get; set; }
    public IpRange[]? PacketCaptureIncludeIpRanges { get; set; }
    public string? GaMeasurementId { get; init;}
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan TcpReuseTimeout { get; init; } = TimeSpan.FromSeconds(60);
    public string? ServerTokenJson { get; set; }
}
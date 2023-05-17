using System;
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
    public IPAddress ClientPublicAddress { get; set; } = null!;

    [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
    public IPEndPoint[] TcpEndPoints { get; set; } = Array.Empty<IPEndPoint>();

    [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
    public IPEndPoint[] UdpEndPoints { get; set; } = Array.Empty<IPEndPoint>();

    public string ServerVersion { get; set; } = null!;
    public int ServerProtocolVersion { get; set; }
    
    public int UdpPort { get; set; }
    public byte[]? UdpKey { get; set; } = null!;
    public byte[]? ServerSecret { get; set; } //todo: must not null in version >= 2.9.362
    public int MaxDatagramChannelCount { get; set; } 
    public bool IsIpV6Supported { get; set; }
    public IpRange[]? IncludeIpRanges { get; set; }
    public IpRange[]? PacketCaptureIncludeIpRanges { get; set; }
}
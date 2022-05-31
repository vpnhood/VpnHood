using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;
using VpnHood.Common.Messaging;

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

    public string ServerVersion { get; set; } = null!;
    public int ServerProtocolVersion { get; set; }
    public int UdpPort { get; set; }
    public byte[]? UdpKey { get; set; } = null!;
    public int MaxDatagramChannelCount { get; set; }

    [JsonConverter(typeof(IPAddressConverter))]
    public IPAddress ClientPublicAddress { get; set; } = null!;
}
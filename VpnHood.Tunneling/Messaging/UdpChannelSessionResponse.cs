using System.Text.Json.Serialization;
using VpnHood.Common.Messaging;

namespace VpnHood.Tunneling.Messaging;

public class UdpChannelSessionResponse : SessionResponseBase
{
    [JsonConstructor]
    public UdpChannelSessionResponse(SessionErrorCode errorCode)
        : base(errorCode)
    {
    }

    public UdpChannelSessionResponse(SessionResponseBase obj)
        : base(obj)
    {
    }

    public int UdpPort { get; set; }
    public byte[] UdpKey { get; set; } = null!;
}
using System.Text.Json.Serialization;
using VpnHood.Common.Messaging;

namespace VpnHood.Tunneling.Messaging;

// todo: deprecated version >= 2.9.362
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
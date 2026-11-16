using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Core.Common.Messaging;

/// <summary>
/// Specifies the protocol of the tunnel's packet channels.
/// </summary>
/// <remarks>
/// In JSON it is a name; a name this version does not know reads as <see cref="Tcp"/>, the protocol every engine and
/// server has.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumFallbackConverter<ChannelProtocol>))]
[JsonEnumFallback(ChannelProtocol.Tcp)]
public enum ChannelProtocol
{
    /// <summary>QUIC.</summary>
    Quic,

    /// <summary>UDP.</summary>
    Udp,

    /// <summary>TCP, over TLS.</summary>
    Tcp
}

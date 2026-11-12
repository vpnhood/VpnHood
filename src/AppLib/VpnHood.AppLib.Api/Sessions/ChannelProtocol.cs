using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.Sessions;

// How the tunnel carries packets. Saved in settings.json as well as sent over the wire, so the
// member names must stay exactly the engine's - the mapper is exhaustive to keep them so.

/// <summary>
/// Specifies the protocol of the tunnel's packet channels.
/// </summary>
/// <remarks>
/// In JSON it is a name; a name this version does not know fails to read.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<ChannelProtocol>))]
public enum ChannelProtocol
{
    /// <summary>QUIC.</summary>
    Quic,

    /// <summary>UDP.</summary>
    Udp,

    /// <summary>TCP, over TLS.</summary>
    Tcp
}

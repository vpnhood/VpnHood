using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Contracts.Sessions;

// How the tunnel carries packets. Saved in settings.json as well as sent over the wire, so the
// member names must stay exactly the engine's - the mapper is exhaustive to keep them so.
[JsonConverter(typeof(JsonStringEnumConverter<ChannelProtocol>))]
public enum ChannelProtocol
{
    Quic,
    Udp,
    Tcp
}

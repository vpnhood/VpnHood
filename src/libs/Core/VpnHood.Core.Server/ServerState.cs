using System.Text.Json.Serialization;

namespace VpnHood.Core.Server;

[JsonConverter(typeof(JsonStringEnumConverter<ServerState>))]
internal enum ServerState
{
    NotStarted,
    Waiting,
    Configuring,
    BadConfig,
    Ready,
    Disposed
}
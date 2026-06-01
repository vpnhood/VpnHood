using System.Text.Json.Serialization;

namespace VpnHood.Core.Server;

[JsonConverter(typeof(JsonStringEnumConverter<ServerState>))]
public enum ServerState
{
    NotStarted,
    Waiting,
    Configuring,
    Ready,
    Disposed
}
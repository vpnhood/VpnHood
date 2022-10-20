using System.Text.Json.Serialization;

namespace VpnHood.AccessServer.ServerUtils;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ServerState
{
    NotInstalled,
    Error,
    Disabled,
    Lost,
    Configuring,
    Idle,
    Active
}
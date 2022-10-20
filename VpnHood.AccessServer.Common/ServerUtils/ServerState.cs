using System.Text.Json.Serialization;

namespace VpnHood.AccessServer.ServerUtils;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ServerState
{
    NotInstalled,
    Disabled,
    Lost,
    Configuring,
    Idle,
    Active
}
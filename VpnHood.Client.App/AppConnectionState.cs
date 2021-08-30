using System.Text.Json.Serialization;

namespace VpnHood.Client.App
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AppConnectionState
    {
        None,
        Diagnosing,
        Connecting,
        Connected,
        Disconnecting
    }
}
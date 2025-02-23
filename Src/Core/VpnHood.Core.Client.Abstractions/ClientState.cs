using System.Text.Json.Serialization;
namespace VpnHood.Core.Client.Abstractions;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ClientState
{
    None,
    Initializing,
    Connecting,
    Connected,
    Waiting,
    WaitingForAd,
    Disconnecting,
    Disposed
}
using System.Text.Json.Serialization;
namespace VpnHood.Core.Client.Abstractions;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ClientState
{
    None,
    Initializing,
    FindingReachableServer,
    FindingBestServer,
    Connecting,
    Connected,
    Unstable,
    Waiting,
    WaitingForAd,
    WaitingForAdEx,
    Disconnecting,
    Disposed
}
using System.Text.Json.Serialization;

namespace VpnHood.AppLib;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AppConnectionState
{
    None,
    Initializing,
    Waiting,
    WaitingForAd,
    Diagnosing,
    FindingReachableServer,
    FindingBestServer,
    Connecting,
    Connected,
    Unstable,
    Disconnecting
}
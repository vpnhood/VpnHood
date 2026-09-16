using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Contracts.App;

[JsonConverter(typeof(JsonStringEnumConverter<AppConnectionState>))]
public enum AppConnectionState
{
    None,
    Initializing,
    Waiting,
    WaitingForAd,
    Diagnosing,
    ValidatingProxies,
    FindingReachableServer,
    FindingBestServer,
    Connecting,
    Connected,
    Unstable,
    Disconnecting
}

using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.App;

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

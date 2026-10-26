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
    Disconnecting,

    // an update this app is installing, which stops it in a moment: no connect until it has
    Updating
}

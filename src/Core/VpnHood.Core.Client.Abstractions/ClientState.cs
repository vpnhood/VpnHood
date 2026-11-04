using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Core.Client.Abstractions;

// A name this side does not know reads as Connecting: a state a newer engine added is one on the way: the app
// shows progress and lets the person disconnect.
[JsonConverter(typeof(JsonStringEnumFallbackConverter<ClientState>))]
[JsonEnumFallback(ClientState.Connecting)]
public enum ClientState
{
    None,
    Initializing,
    ValidatingProxies,
    FindingReachableServer,
    FindingBestServer,
    Connecting,
    Connected,
    Unstable,
    Waiting,
    WaitingForAd,
    Disconnecting,
    Disposed
}

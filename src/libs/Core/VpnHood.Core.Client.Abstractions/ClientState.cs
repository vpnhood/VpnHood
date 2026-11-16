using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Core.Client.Abstractions;

/// <summary>
/// Specifies the state of a VPN client.
/// </summary>
/// <remarks>
/// In JSON it is a name; a name this version does not know reads as <see cref="Connecting"/>, a state on the way, in
/// which the app shows progress and lets the person disconnect.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumFallbackConverter<ClientState>))]
[JsonEnumFallback(ClientState.Connecting)]
public enum ClientState
{
    /// <summary>Not started.</summary>
    None,

    /// <summary>Starting.</summary>
    Initializing,

    /// <summary>Checking the proxies it connects through.</summary>
    ValidatingProxies,

    /// <summary>Finding a server that answers.</summary>
    FindingReachableServer,

    /// <summary>Finding the best of the servers that answer.</summary>
    FindingBestServer,

    /// <summary>Making the session.</summary>
    Connecting,

    /// <summary>Connected.</summary>
    Connected,

    /// <summary>
    /// Connected, but a request to the server failed: a successful one brings the client back to
    /// <see cref="Connected"/>, and failures past <see cref="ClientTransportOptions.UnstableTimeout"/> make it wait.
    /// </summary>
    Unstable,

    /// <summary>
    /// Paused, as the server was not reached for longer than <see cref="ClientTransportOptions.UnstableTimeout"/>: the
    /// device's packets are dropped until <see cref="ClientTransportOptions.AutoWaitTimeout"/> passes, and the next one
    /// tries again.
    /// </summary>
    Waiting,

    /// <summary>Waiting for the ad the session requires.</summary>
    WaitingForAd,

    /// <summary>Disconnecting.</summary>
    Disconnecting,

    /// <summary>Disposed.</summary>
    Disposed
}

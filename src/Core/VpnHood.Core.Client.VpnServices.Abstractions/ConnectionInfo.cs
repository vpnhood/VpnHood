using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Net.Toolkit.ApiClients;
using VpnHood.Net.Toolkit.Monitoring;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Client.VpnServices.Abstractions;

/// <summary>
/// Represents the VPN service's connection as it reports it to the app: its state, its session, and its error.
/// </summary>
/// <remarks>
/// The service writes it to its config folder as <see cref="ClientOptions.VpnStatusFileName"/> at each state change,
/// and sends it with each answer (<see cref="Requests.ApiResponse{T}.ConnectionInfo"/>).
/// </remarks>
public class ConnectionInfo
{
    /// <summary>
    /// Gets the placeholder before the VPN service has made a real one: <see cref="ClientState.Initializing"/>, with its
    /// creation time and nothing else. Every answer must carry a connection's info, so transports and the host answer
    /// with this until then.
    /// </summary>
    public static ConnectionInfo Default { get; } = new() {
        ProxyConnectorStatus = null,
        ClientState = ClientState.Initializing,
        ClientStateProgress = null,
        ClientStateChangedTime = null,
        CreatedTime = FastDateTime.UtcNow,
        Error = null,
        SessionInfo = null,
        SessionName = null,
        SessionStatus = null
    };

    /// <summary>
    /// Gets the state of the proxies the client connects through, or <see langword="null"/> where it uses none.
    /// </summary>
    public required ProxyConnectorStatus? ProxyConnectorStatus { get; init; }

    /// <summary>
    /// Gets when the VPN service made this info, in UTC, or <see langword="null"/> where the app made it itself.
    /// </summary>
    public required DateTime? CreatedTime { get; init; }

    /// <summary>
    /// Gets the name the VPN adapter gives the session where the OS shows one
    /// (<see cref="ClientOptions.SessionName"/>), or <see langword="null"/>.
    /// </summary>
    public string? SessionName { get; init; }

    /// <summary>Gets the client's state.</summary>
    public required ClientState ClientState { get; init; }

    /// <summary>
    /// Gets the progress of the state, while the client finds a server or checks its proxies, or
    /// <see langword="null"/>.
    /// </summary>
    public required ProgressStatus? ClientStateProgress { get; init; }

    /// <summary>Gets when the state last changed, in UTC, or <see langword="null"/>.</summary>
    public required DateTime? ClientStateChangedTime { get; init; }

    /// <summary>Gets the error the connection failed or ended with, or <see langword="null"/>.</summary>
    public required ApiError? Error { get; init; }

    /// <summary>
    /// Gets the session as its server set it up, or <see langword="null"/> where there is none, before the session or
    /// after it.
    /// </summary>
    public required SessionInfo? SessionInfo { get; init; }

    /// <summary>
    /// Gets the session's state, or <see langword="null"/> where there is no session, before it or after it.
    /// </summary>
    public required SessionStatus? SessionStatus { get; init; }

    /// <summary>
    /// Gets the engine's report of itself, or <see langword="null"/> where the app speaks for it, before any status.
    /// </summary>
    public VpnEngineInfo? VpnEngineInfo { get; init; }

    /// <summary>
    /// Determines whether the connection is started: its state is none of <see cref="ClientState.None"/>,
    /// <see cref="ClientState.Disposed"/> and <see cref="ClientState.Disconnecting"/>.
    /// </summary>
    /// <returns><see langword="true"/> if the connection is started; otherwise, <see langword="false"/>.</returns>
    public bool IsStarted() => ClientState is not (
        ClientState.None or ClientState.Disposed or ClientState.Disconnecting);
}
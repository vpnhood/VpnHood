using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Core.Toolkit.ApiClients;
using VpnHood.Core.Toolkit.Monitoring;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Client.VpnServices.Abstractions;

public class ConnectionInfo
{
    // The pre-start placeholder. Every ApiResponse must carry a ConnectionInfo, so transports and the
    // host answer with this until the VPN service has produced a real one.
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

    public required ProxyConnectorStatus? ProxyConnectorStatus { get; init; }
    public required DateTime? CreatedTime { get; init; }
    public string? SessionName { get; init; }
    public required ClientState ClientState { get; init; }
    public required ProgressStatus? ClientStateProgress { get; init; }
    public required DateTime? ClientStateChangedTime { get; init; }
    public required ApiError? Error { get; init; }
    public required SessionInfo? SessionInfo { get; init; }
    public required SessionStatus? SessionStatus { get; init; }

    public bool IsStarted() => ClientState is not (
        ClientState.None or ClientState.Disposed or ClientState.Disconnecting);
}
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Proxies.EndPointManagement.Abstractions;
using VpnHood.Core.Toolkit.ApiClients;
using VpnHood.Core.Toolkit.Monitoring;

namespace VpnHood.Core.Client.VpnServices.Abstractions;

public class ConnectionInfo
{
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
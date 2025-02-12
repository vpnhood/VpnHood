using System.Net;
using VpnHood.Core.Common.ApiClients;

namespace VpnHood.Core.Client.Abstractions;

public class ConnectionInfo
{
    public required ClientState ClientState { get; init; }
    public required ApiError? Error { get; init; }
    public required SessionInfo? SessionInfo { get; init; }
    public required SessionStatus? SessionStatus { get; init; }
    public required IPEndPoint? ApiEndPoint { get; init; }
    public required byte[]? ApiKey { get; init; }

    public bool IsStarted() => ClientState is not (ClientState.None or ClientState.Disposed);
}
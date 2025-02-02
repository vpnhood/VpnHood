using VpnHood.Core.Common.ApiClients;
using VpnHood.Core.Common.Messaging;

namespace VpnHood.Core.Client.Abstractions;

public class ConnectionInfo
{
    public required ClientState ClientState { get; init; }
    public required SessionErrorCode ErrorCode { get; init; }
    public required ApiError? Error { get; init; }
    public required SessionInfo? SessionInfo { get; init; }
    public required SessionStatus? SessionStatus { get; init; }
}
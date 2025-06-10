using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Toolkit.ApiClients;
using VpnHood.Core.Toolkit.Converters;

namespace VpnHood.Core.Client.VpnServices.Abstractions;

public class ConnectionInfo
{
    [JsonConverter(typeof(IPEndPointConverter))]
    public required DateTime? CreatedTime { get; init; }
    public required IPEndPoint? ApiEndPoint { get; init; }
    public string? SessionName { get; init; }
    public required ClientState ClientState { get; init; }
    public required ApiError? Error { get; init; }
    public required SessionInfo? SessionInfo { get; init; }
    public required SessionStatus? SessionStatus { get; init; }
    public required byte[]? ApiKey { get; init; }
    public bool IsStarted() => ClientState is not (
        ClientState.None or ClientState.Disposed or ClientState.Disconnecting);
}

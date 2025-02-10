using System.Net;
using VpnHood.Core.Common.ApiClients;

namespace VpnHood.Core.Client.Abstractions;

public class VpnServiceStatus
{
    public required IPEndPoint? ApiEndPoint { get; init; }
    public required byte[]? ApiKey { get; init; }
    public required ApiError? Error { get; init; }
}
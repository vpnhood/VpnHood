using VpnHood.Core.Common.ApiClients;

namespace VpnHood.Core.Client.Abstractions.ApiRequests;

public class ApiSetAdResultRequest : IApiRequest
{
    public required AdResult? AdResult { get; init; }
    public required ApiError? ApiError { get; init; }
}
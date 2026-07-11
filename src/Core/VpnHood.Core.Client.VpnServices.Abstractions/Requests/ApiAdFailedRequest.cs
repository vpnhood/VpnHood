using VpnHood.Core.Toolkit.ApiClients;

namespace VpnHood.Core.Client.VpnServices.Abstractions.Requests;

public class ApiAdFailedRequest : IApiRequest
{
    public required ApiError? ApiError { get; init; }
}
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Toolkit.ApiClients;

namespace VpnHood.Core.Client.VpnServices.Abstractions.Requests;

public class ApiSetAdResultRequest : IApiRequest
{
    public required bool IsRewarded { get; init; }
    public required AdResult? AdResult { get; init; }
    public required ApiError? ApiError { get; init; }
}
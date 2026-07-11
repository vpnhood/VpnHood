using VpnHood.Core.Client.Abstractions;

namespace VpnHood.Core.Client.VpnServices.Abstractions.Requests;

public class ApiSetAdOkRequest : IApiRequest
{
    public required bool IsRewarded { get; init; }
    public required AdResult AdResult { get; init; }
}
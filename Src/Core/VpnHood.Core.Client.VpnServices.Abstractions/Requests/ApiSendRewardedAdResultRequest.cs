using VpnHood.Core.Client.Abstractions;

namespace VpnHood.Core.Client.VpnServices.Abstractions.Requests;

public class ApiSendRewardedAdResultRequest : IApiRequest
{
    public required AdResult AdResult { get; init; }
}
namespace VpnHood.Core.Client.Abstractions.ApiRequests;

public class ApiSendRewardedAdResultRequest : IApiRequest
{
    public required AdResult AdResult { get; init; }
}
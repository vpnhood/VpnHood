namespace VpnHood.Core.Client.Abstractions.ApiRequests;

public class ApiRewardedAdResult : IApiRequest
{
    public required AdResult AdResult { get; init; }
}
namespace VpnHood.Core.Client.Abstractions.ApiRequests;

public class ApiReconfigureRequest : IApiRequest
{
    public required ClientReconfigureParams ReconfigureParams { get; init; }
}
namespace VpnHood.Core.Client.VpnServices.Abstractions.Requests;

public class ApiReconfigureRequest : IApiRequest
{
    public required ClientReconfigureParams Params { get; init; }
}
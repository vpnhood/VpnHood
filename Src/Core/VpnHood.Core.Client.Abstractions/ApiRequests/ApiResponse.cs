using VpnHood.Core.Common.ApiClients;

namespace VpnHood.Core.Client.Abstractions.ApiRequests;

public class ApiResponse<T>
{
    public required ConnectionInfo ConnectionInfo { get; set; }
    public required ApiError? ApiError { get; set; }
    public required T? Result { get; set; }
}

using VpnHood.Core.Toolkit.ApiClients;

namespace VpnHood.Core.Client.VpnServices.Abstractions.Requests;

public class ApiResponse<T>
{
    public required ConnectionInfo ConnectionInfo { get; set; }
    public required ApiError? ApiError { get; set; }
    public required T? Result { get; set; }
}

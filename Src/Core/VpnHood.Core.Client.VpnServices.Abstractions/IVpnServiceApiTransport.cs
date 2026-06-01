using VpnHood.Core.Client.VpnServices.Abstractions.Requests;

namespace VpnHood.Core.Client.VpnServices.Abstractions;

public interface IVpnServiceApiTransport : IDisposable
{
    Task<ApiResponse<T>> SendRequest<T>(ConnectionInfo connectionInfo, IApiRequest request,
        CancellationToken cancellationToken);

    //todo: looks redundant
    void Reset();
}
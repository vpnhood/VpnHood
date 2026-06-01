using VpnHood.Core.Client.VpnServices.Abstractions.Requests;

namespace VpnHood.Core.Client.VpnServices.Abstractions;

public interface IVpnServiceApiTransport : IDisposable
{
    Task<ApiResponse<T>> SendRequestAsync<T>(ConnectionInfo connectionInfo, IApiRequest request,
        CancellationToken cancellationToken);

    void Reset();
}
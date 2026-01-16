using VpnHood.AppLib.Services.Proxies;
using VpnHood.Core.Proxies.EndPointManagement.Abstractions;

namespace VpnHood.AppLib.WebServer.Api;

public interface IProxyEndPointController
{
    Task<AppProxyEndPointInfo> Get(string proxyEndPointId, CancellationToken cancellationToken);
    Task<AppProxyEndPointInfo> Add(ProxyEndPoint proxyEndPoint, CancellationToken cancellationToken);
    Task<AppProxyEndPointInfo> Update(string proxyEndPointId, ProxyEndPoint proxyEndPoint, CancellationToken cancellationToken);
    Task Delete(string proxyEndPointId, CancellationToken cancellationToken);
    Task DeleteAll(bool deleteSucceeded, bool deleteFailed, bool deleteUnknown, bool deleteDisabled, CancellationToken cancellationToken);
    Task DisableAllFailed(CancellationToken cancellationToken);
    Task ResetStates(CancellationToken cancellationToken);
    Task<AppProxyEndPointInfo?> GetDevice(CancellationToken cancellationToken);
    Task<AppProxyEndPointInfo[]> List(
        bool includeSucceeded,
        bool includeFailed,
        bool includeUnknown,
        bool includeDisabled,
        int? recordIndex,
        int? recordCount,
        CancellationToken cancellationToken);
    Task Import(string content, CancellationToken cancellationToken);
    Task ReloadUrl(CancellationToken cancellationToken);
    Task<AppProxyEndPointInfo> Parse(string text, ProxyEndPointDefaults defaults, CancellationToken cancellationToken);
}
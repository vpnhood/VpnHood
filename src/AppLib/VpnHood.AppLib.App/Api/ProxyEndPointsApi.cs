using VpnHood.AppLib.Api.Proxies;
using VpnHood.AppLib.DtoConverters;
using VpnHood.AppLib.Services.Proxies;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Core.Toolkit.Generics;
using ProxyEndPoint = VpnHood.AppLib.Api.Proxies.ProxyEndPoint;
using ProxyEndPointDefaults = VpnHood.AppLib.Api.Proxies.ProxyEndPointDefaults;

namespace VpnHood.AppLib.Api;

internal sealed class ProxyEndPointsApi(VpnHoodApp app) : IProxyEndPointsApi
{
    private AppProxyEndPointService ProxyEndPointService => app.Services.ProxyEndPointService;

    public Task ResetStates(CancellationToken cancellationToken)
    {
        return ProxyEndPointService.ResetStates();
    }

    public Task<AppProxyEndPointInfo?> GetDevice(CancellationToken cancellationToken)
    {
        var result = ProxyEndPointService.GetDeviceProxy();
        return Task.FromResult(result);
    }

    public Task<ListResult<AppProxyEndPointInfo>> List(
        string? search,
        bool includeSucceeded,
        bool includeFailed,
        bool includeUnknown,
        bool includeDisabled,
        int? recordIndex,
        int? recordCount,
        CancellationToken cancellationToken)
    {
        return ProxyEndPointService.ListProxies(
            search: search,
            includeSucceeded: includeSucceeded,
            includeFailed: includeFailed,
            includeUnknown: includeUnknown,
            includeDisabled: includeDisabled,
            recordIndex: recordIndex,
            recordCount: recordCount);
    }

    public Task<AppProxyEndPointInfo> Get(string proxyEndPointId, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return ProxyEndPointService.Get(proxyEndPointId);
    }

    public Task<AppProxyEndPointInfo> Parse(string text, ProxyEndPointDefaults defaults, CancellationToken cancellationToken)
    {
        var parsed = ProxyEndPointParser.ParseHostToUrl(text, defaults.ToEngine());
        var endpoint = ProxyEndPointParser.FromUrl(parsed);
        var info = new AppProxyEndPointInfo {
            EndPoint = endpoint.ToAppDto(),
            CountryCode = null
        };

        return Task.FromResult(info);
    }

    public Task<AppProxyEndPointInfo> Update(string proxyEndPointId, ProxyEndPoint proxyEndPoint, CancellationToken cancellationToken)
    {
        return ProxyEndPointService.Update(proxyEndPointId, proxyEndPoint);
    }

    public Task<AppProxyEndPointInfo> Add(ProxyEndPoint proxyEndPoint, CancellationToken cancellationToken)
    {
        return ProxyEndPointService.Add(proxyEndPoint);
    }

    public Task Delete(string proxyEndPointId, CancellationToken cancellationToken)
    {
        return ProxyEndPointService.Delete(proxyEndPointId);
    }

    public Task DeleteAll(
        bool deleteSucceeded,
        bool deleteFailed,
        bool deleteUnknown,
        bool deleteDisabled,
        CancellationToken cancellationToken)
    {
        return ProxyEndPointService.DeleteAll(new DeleteAllOptions {
            DeleteSucceeded = deleteSucceeded,
            DeleteFailed = deleteFailed,
            DeleteUnknown = deleteUnknown,
            DeleteDisabled = deleteDisabled
        });
    }

    public Task Import(string content, CancellationToken cancellationToken)
    {
        return ProxyEndPointService.Import(content);
    }

    public Task DisableAllFailed(CancellationToken cancellationToken)
    {
        return ProxyEndPointService.DisableAllFailed();
    }

    public Task ReloadUrl(CancellationToken cancellationToken)
    {
        return ProxyEndPointService.ReloadUrl(cancellationToken);
    }
}
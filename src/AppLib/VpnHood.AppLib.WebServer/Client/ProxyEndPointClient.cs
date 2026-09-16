using VpnHood.AppLib.Services.Proxies;
using VpnHood.AppLib.WebServer.Api;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Core.Toolkit.Generics;

namespace VpnHood.AppLib.WebServer.Client;

// IProxyEndPointController over HTTP: the routes of ProxyEndPointController, one for one.
internal sealed class ProxyEndPointClient(HttpClient httpClient) : AppApiClientBase(httpClient), IProxyEndPointController
{
    private const string BaseUrl = "api/proxy-endpoints/";

    public Task<AppProxyEndPointInfo> Get(string proxyEndPointId, CancellationToken cancellationToken)
    {
        return GetAsync<AppProxyEndPointInfo>(BaseUrl + Segment(proxyEndPointId), null, cancellationToken);
    }

    public Task<AppProxyEndPointInfo> Add(ProxyEndPoint proxyEndPoint, CancellationToken cancellationToken)
    {
        return PostAsync<ProxyEndPoint, AppProxyEndPointInfo>(BaseUrl, null, proxyEndPoint, cancellationToken);
    }

    public Task<AppProxyEndPointInfo> Update(string proxyEndPointId, ProxyEndPoint proxyEndPoint, CancellationToken cancellationToken)
    {
        return PutAsync<ProxyEndPoint, AppProxyEndPointInfo>(BaseUrl + Segment(proxyEndPointId), proxyEndPoint, cancellationToken);
    }

    public Task Delete(string proxyEndPointId, CancellationToken cancellationToken)
    {
        return DeleteAsync(BaseUrl + Segment(proxyEndPointId), null, cancellationToken);
    }

    public Task DeleteAll(bool deleteSucceeded, bool deleteFailed, bool deleteUnknown, bool deleteDisabled, CancellationToken cancellationToken)
    {
        return DeleteAsync(BaseUrl, new Dictionary<string, object?> {
            ["deleteSucceeded"] = deleteSucceeded,
            ["deleteFailed"] = deleteFailed,
            ["deleteUnknown"] = deleteUnknown,
            ["deleteDisabled"] = deleteDisabled
        }, cancellationToken);
    }

    public Task DisableAllFailed(CancellationToken cancellationToken)
    {
        return PostAsync(BaseUrl + "disable-failed", null, cancellationToken);
    }

    public Task ResetStates(CancellationToken cancellationToken)
    {
        return PostAsync(BaseUrl + "reset-states", null, cancellationToken);
    }

    // No device proxy is an empty reply, not a failure.
    public Task<AppProxyEndPointInfo?> GetDevice(CancellationToken cancellationToken)
    {
        return GetOrDefaultAsync<AppProxyEndPointInfo>(BaseUrl + "device", cancellationToken);
    }

    public Task<ListResult<AppProxyEndPointInfo>> List(string? search, bool includeSucceeded, bool includeFailed,
        bool includeUnknown, bool includeDisabled, int? recordIndex, int? recordCount, CancellationToken cancellationToken)
    {
        return GetAsync<ListResult<AppProxyEndPointInfo>>(BaseUrl, new Dictionary<string, object?> {
            ["search"] = search,
            ["includeSucceeded"] = includeSucceeded,
            ["includeFailed"] = includeFailed,
            ["includeUnknown"] = includeUnknown,
            ["includeDisabled"] = includeDisabled,
            ["recordIndex"] = recordIndex,
            ["recordCount"] = recordCount
        }, cancellationToken);
    }

    public Task Import(string content, CancellationToken cancellationToken)
    {
        return PostBodyAsync(BaseUrl + "import", content, cancellationToken);
    }

    public Task ReloadUrl(CancellationToken cancellationToken)
    {
        return PostAsync(BaseUrl + "reload-url", null, cancellationToken);
    }

    public Task<AppProxyEndPointInfo> Parse(string text, ProxyEndPointDefaults defaults, CancellationToken cancellationToken)
    {
        return PostAsync<ProxyEndPointDefaults, AppProxyEndPointInfo>(BaseUrl + "parse",
            new Dictionary<string, object?> { ["text"] = text }, defaults, cancellationToken);
    }
}

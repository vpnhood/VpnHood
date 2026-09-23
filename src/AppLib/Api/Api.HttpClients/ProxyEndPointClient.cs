using VpnHood.AppLib.Api.Proxies;
using VpnHood.Core.Toolkit.Generics;

namespace VpnHood.AppLib.Api.HttpClients;

// IProxyEndPointsApi over HTTP: the routes of ProxyEndPointController, one for one.
internal sealed class ProxyEndPointClient(HttpClient httpClient) : AppApiClientBase(httpClient), IProxyEndPointsApi
{
    private const string BaseUrl = "api/proxy-endpoints/";

    public Task<AppProxyEndPointInfo> Get(string proxyEndPointId, CancellationToken cancellationToken)
    {
        return HttpGetAsync<AppProxyEndPointInfo>(BaseUrl + Uri.EscapeDataString(proxyEndPointId), null, cancellationToken);
    }

    public Task<AppProxyEndPointInfo> Add(ProxyEndPoint proxyEndPoint, CancellationToken cancellationToken)
    {
        return HttpPostAsync<AppProxyEndPointInfo>(BaseUrl, null, proxyEndPoint, cancellationToken);
    }

    public Task<AppProxyEndPointInfo> Update(string proxyEndPointId, ProxyEndPoint proxyEndPoint, CancellationToken cancellationToken)
    {
        return HttpPutAsync<AppProxyEndPointInfo>(BaseUrl + Uri.EscapeDataString(proxyEndPointId), null, proxyEndPoint, cancellationToken);
    }

    public Task Delete(string proxyEndPointId, CancellationToken cancellationToken)
    {
        return HttpDeleteAsync(BaseUrl + Uri.EscapeDataString(proxyEndPointId), null, cancellationToken);
    }

    public Task DeleteAll(bool deleteSucceeded, bool deleteFailed, bool deleteUnknown, bool deleteDisabled, CancellationToken cancellationToken)
    {
        return HttpDeleteAsync(BaseUrl, new Dictionary<string, object?> {
            ["deleteSucceeded"] = deleteSucceeded, ["deleteFailed"] = deleteFailed, ["deleteUnknown"] = deleteUnknown, ["deleteDisabled"] = deleteDisabled
        }, cancellationToken);
    }

    public Task DisableAllFailed(CancellationToken cancellationToken)
    {
        return HttpPostAsync(BaseUrl + "disable-failed", null, null, cancellationToken);
    }

    public Task ResetStates(CancellationToken cancellationToken)
    {
        return HttpPostAsync(BaseUrl + "reset-states", null, null, cancellationToken);
    }

    // No device proxy is an empty reply, not a failure.
    public Task<AppProxyEndPointInfo?> GetDevice(CancellationToken cancellationToken)
    {
        return HttpGetAsync<AppProxyEndPointInfo?>(BaseUrl + "device", null, cancellationToken);
    }

    public Task<ListResult<AppProxyEndPointInfo>> List(string? search, bool includeSucceeded, bool includeFailed,
        bool includeUnknown, bool includeDisabled, int? recordIndex, int? recordCount, CancellationToken cancellationToken)
    {
        return HttpGetAsync<ListResult<AppProxyEndPointInfo>>(BaseUrl, new Dictionary<string, object?> {
            ["search"] = search, ["includeSucceeded"] = includeSucceeded, ["includeFailed"] = includeFailed, ["includeUnknown"] = includeUnknown, ["includeDisabled"] = includeDisabled, ["recordIndex"] = recordIndex, ["recordCount"] = recordCount
        }, cancellationToken);
    }

    public Task Import(string content, CancellationToken cancellationToken)
    {
        return HttpPostAsync(BaseUrl + "import", null, content, cancellationToken);
    }

    public Task ReloadUrl(CancellationToken cancellationToken)
    {
        return HttpPostAsync(BaseUrl + "reload-url", null, null, cancellationToken);
    }

    public Task<AppProxyEndPointInfo> Parse(string text, ProxyEndPointDefaults defaults, CancellationToken cancellationToken)
    {
        return HttpPostAsync<AppProxyEndPointInfo>(BaseUrl + "parse", new Dictionary<string, object?> { ["text"] = text }, defaults, cancellationToken);
    }
}

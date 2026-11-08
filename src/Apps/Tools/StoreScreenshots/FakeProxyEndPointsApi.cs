using VpnHood.AppLib.Api;
using VpnHood.AppLib.Api.Proxies;
using VpnHood.Net.Toolkit.Generics;

namespace VpnHood.App.StoreScreenshots;

// The proxy list from the fixture (StoreFixture.ProxyEndPoints): empty, as the Proxies page is on a
// fresh install, unless a patch gives one. The page's status filter and paging are answered as the
// app answers them, so a list longer than a page pages.
internal sealed class FakeProxyEndPointsApi(IReadOnlyList<AppProxyEndPointInfo> proxies) : IProxyEndPointsApi
{
    public Task<ListResult<AppProxyEndPointInfo>> List(string? search, bool includeSucceeded, bool includeFailed, bool includeUnknown,
        bool includeDisabled, int? recordIndex, int? recordCount, CancellationToken cancellationToken)
    {
        var matches = proxies.Where(x =>
            !x.EndPoint.IsEnabled ? includeDisabled
            : x.Status.FailedCount > x.Status.SucceededCount ? includeFailed
            : x.Status.SucceededCount > 0 ? includeSucceeded
            : includeUnknown).ToArray();
        var items = matches.Skip(recordIndex ?? 0).Take(recordCount ?? matches.Length).ToArray();
        return Task.FromResult(new ListResult<AppProxyEndPointInfo> { Items = items, TotalCount = matches.Length });
    }

    public Task<AppProxyEndPointInfo?> GetDevice(CancellationToken cancellationToken) => Task.FromResult<AppProxyEndPointInfo?>(null);

    public Task<AppProxyEndPointInfo> Get(string proxyEndPointId, CancellationToken cancellationToken) => throw UnmockedCalls.Record("ProxyEndPoints.Get");
    public Task<AppProxyEndPointInfo> Add(ProxyEndPoint proxyEndPoint, CancellationToken cancellationToken) => throw UnmockedCalls.Record("ProxyEndPoints.Add");
    public Task<AppProxyEndPointInfo> Update(string proxyEndPointId, ProxyEndPoint proxyEndPoint, CancellationToken cancellationToken) => throw UnmockedCalls.Record("ProxyEndPoints.Update");
    public Task Delete(string proxyEndPointId, CancellationToken cancellationToken) => throw UnmockedCalls.Record("ProxyEndPoints.Delete");
    public Task DeleteAll(bool deleteSucceeded, bool deleteFailed, bool deleteUnknown, bool deleteDisabled, CancellationToken cancellationToken) => throw UnmockedCalls.Record("ProxyEndPoints.DeleteAll");
    public Task DisableAllFailed(CancellationToken cancellationToken) => throw UnmockedCalls.Record("ProxyEndPoints.DisableAllFailed");
    public Task ResetStates(CancellationToken cancellationToken) => throw UnmockedCalls.Record("ProxyEndPoints.ResetStates");
    public Task Import(string content, CancellationToken cancellationToken) => throw UnmockedCalls.Record("ProxyEndPoints.Import");
    public Task ReloadUrl(CancellationToken cancellationToken) => throw UnmockedCalls.Record("ProxyEndPoints.ReloadUrl");
    public Task<AppProxyEndPointInfo> Parse(string text, ProxyEndPointDefaults defaults, CancellationToken cancellationToken) => throw UnmockedCalls.Record("ProxyEndPoints.Parse");
}

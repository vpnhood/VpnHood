using VpnHood.AppLib.Api;
using VpnHood.AppLib.Api.Proxies;
using VpnHood.Core.Toolkit.Generics;

namespace VpnHood.App.StoreScreenshots;

// An empty proxy list, which is what the Proxies page shows on a fresh install - the fixture
// records no proxies, and the web UI engine's screenshot of that page shows none either.
internal sealed class FakeProxyEndPointsApi : IProxyEndPointsApi
{
    public Task<ListResult<AppProxyEndPointInfo>> List(string? search, bool includeSucceeded, bool includeFailed, bool includeUnknown,
        bool includeDisabled, int? recordIndex, int? recordCount, CancellationToken cancellationToken)
    {
        return Task.FromResult(new ListResult<AppProxyEndPointInfo> { Items = [], TotalCount = 0 });
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

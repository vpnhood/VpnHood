using VpnHood.AppLib.Services.Proxies;
using VpnHood.AppLib.WebServer.Api;
using VpnHood.AppLib.WebServer.Helpers;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Core.Toolkit.Generics;
using VpnHood.Core.Toolkit.Utils;
using HttpMethod = WatsonWebserver.Core.HttpMethod;

namespace VpnHood.AppLib.WebServer.Controllers;

internal class ProxyEndPointController : ControllerBase, IProxyEndPointController
{
    private static AppProxyEndPointService ProxyEndPointService => VpnHoodApp.Instance.Services.ProxyEndPointService;

    public override void AddRoutes(IRouteMapper mapper)
    {
        const string baseUrl = "/api/proxy-endpoints/";

        // Get device proxy
        mapper.AddStatic(HttpMethod.GET, baseUrl + "device", async ctx => {
            var res = await GetDevice(ctx.Token);
            await ctx.SendJson(res);
        });

        // List
        mapper.AddStatic(HttpMethod.GET, baseUrl, async ctx => {
            var search = ctx.GetQueryParameter<string?>("search", null);
            var includeSucceeded = ctx.GetQueryParameter("includeSucceeded", true);
            var includeFailed = ctx.GetQueryParameter("includeFailed", true);
            var includeUnknown = ctx.GetQueryParameter("includeUnknown", true);
            var includeDisabled = ctx.GetQueryParameter("includeDisabled", true);
            var recordIndex = ctx.GetQueryParameter<int?>("recordIndex", null);
            var recordCount = ctx.GetQueryParameter<int?>("recordCount", null);
            
            var res = await List(
                search: search,
                includeSucceeded: includeSucceeded,
                includeFailed: includeFailed,
                includeUnknown: includeUnknown,
                includeDisabled: includeDisabled,
                recordIndex: recordIndex,
                recordCount: recordCount,
                cancellationToken: ctx.Token);
            await ctx.SendJson(res);
        });

        // Get by id
        mapper.AddParam(HttpMethod.GET, baseUrl + "{proxyEndPointId}", async ctx => {
            var id = ctx.GetRouteParameter<string>("proxyEndPointId");
            var res = await Get(id, ctx.Token);
            await ctx.SendJson(res);
        });

        // Add
        mapper.AddStatic(HttpMethod.POST, baseUrl, async ctx => {
            var body = ctx.ReadJson<ProxyEndPoint>();
            var res = await Add(body, ctx.Token);
            await ctx.SendJson(res);
        });

        // Update
        mapper.AddParam(HttpMethod.PUT, baseUrl + "{proxyEndPointId}", async ctx => {
            var id = ctx.GetRouteParameter<string>("proxyEndPointId");
            var body = ctx.ReadJson<ProxyEndPoint>();
            var res = await Update(id, body, ctx.Token);
            await ctx.SendJson(res);
        });

        // Delete
        mapper.AddParam(HttpMethod.DELETE, baseUrl + "{proxyEndPointId}", async ctx => {
            var id = ctx.GetRouteParameter<string>("proxyEndPointId");
            await Delete(id, ctx.Token);
            await ctx.SendNoContent();
        });

        // Delete all
        mapper.AddStatic(HttpMethod.DELETE, baseUrl, async ctx => {
            var deleteSucceeded = ctx.GetQueryParameter("deleteSucceeded", true);
            var deleteFailed = ctx.GetQueryParameter("deleteFailed", true);
            var deleteUnknown = ctx.GetQueryParameter("deleteUnknown", true);
            var deleteDisabled = ctx.GetQueryParameter("deleteDisabled", true);
            await DeleteAll(
                deleteSucceeded: deleteSucceeded,
                deleteFailed: deleteFailed,
                deleteUnknown: deleteUnknown,
                deleteDisabled: deleteDisabled,
                cancellationToken: ctx.Token);

            await ctx.SendNoContent();
        });

        // Disable all failed
        mapper.AddStatic(HttpMethod.POST, baseUrl + "disable-failed", async ctx => {
            await DisableAllFailed(ctx.Token);
            await ctx.SendNoContent();
        });

        // Parse (query: text, body: ProxyEndPointDefaults)
        mapper.AddStatic(HttpMethod.POST, baseUrl + "parse", async ctx => {
            var text = ctx.GetQueryParameter<string>("text");
            var defaults = ctx.ReadJson<ProxyEndPointDefaults>();
            var res = await Parse(text, defaults, ctx.Token);
            await ctx.SendJson(res);
        });

        // Import (query: removeOld, body raw text list)
        mapper.AddStatic(HttpMethod.POST, baseUrl + "import", async ctx => {
            var text = ctx.ReadJson<string>();
            await Import(text, ctx.Token);
            await ctx.SendNoContent();
        });

        // Reset state
        mapper.AddStatic(HttpMethod.POST, baseUrl + "reset-states", async ctx => {
            await ResetStates(ctx.Token);
            await ctx.SendNoContent();
        });

        // Reload from URL
        mapper.AddStatic(HttpMethod.POST, baseUrl + "reload-url", async ctx => {
            await ReloadUrl(ctx.Token);
            await ctx.SendNoContent();
        });
    }

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
        var parsed = ProxyEndPointParser.ParseHostToUrl(text, defaults);
        var endpoint = ProxyEndPointParser.FromUrl(parsed);
        var info = new AppProxyEndPointInfo {
            EndPoint = endpoint,
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
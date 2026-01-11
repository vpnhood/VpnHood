using VpnHood.AppLib.Services.Proxies;
using VpnHood.AppLib.WebServer.Api;
using VpnHood.AppLib.WebServer.Helpers;
using VpnHood.Core.Proxies.EndPointManagement.Abstractions;
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
            var res = await GetDevice();
            await ctx.SendJson(res);
        });

        // List
        mapper.AddStatic(HttpMethod.GET, baseUrl, async ctx => {
            var includeSucceeded = ctx.GetQueryParameter("includeSucceeded", true);
            var includeFailed = ctx.GetQueryParameter("includeFailed", true);
            var includeUnknown = ctx.GetQueryParameter("includeUnknown", true);
            var includeDisabled = ctx.GetQueryParameter("includeDisabled", true);
            var recordIndex = ctx.GetQueryParameter<int?>("recordIndex", null);
            var recordCount = ctx.GetQueryParameter<int?>("recordCount", null);
            
            var res = await List(
                includeSucceeded: includeSucceeded,
                includeFailed: includeFailed,
                includeUnknown: includeUnknown,
                includeDisabled: includeDisabled,
                recordIndex: recordIndex,
                recordCount: recordCount);
            await ctx.SendJson(res);
        });

        // Get by id
        mapper.AddParam(HttpMethod.GET, baseUrl + "{proxyEndPointId}", async ctx => {
            var id = ctx.GetRouteParameter<string>("proxyEndPointId");
            var res = await Get(id, CancellationToken.None);
            await ctx.SendJson(res);
        });

        // Add
        mapper.AddStatic(HttpMethod.POST, baseUrl, async ctx => {
            var body = ctx.ReadJson<ProxyEndPoint>();
            var res = await Add(body);
            await ctx.SendJson(res);
        });

        // Update
        mapper.AddParam(HttpMethod.PUT, baseUrl + "{proxyEndPointId}", async ctx => {
            var id = ctx.GetRouteParameter<string>("proxyEndPointId");
            var body = ctx.ReadJson<ProxyEndPoint>();
            var res = await Update(id, body);
            await ctx.SendJson(res);
        });

        // Delete
        mapper.AddParam(HttpMethod.DELETE, baseUrl + "{proxyEndPointId}", async ctx => {
            var id = ctx.GetRouteParameter<string>("proxyEndPointId");
            await Delete(id);
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
                deleteDisabled: deleteDisabled);

            await ctx.SendNoContent();
        });

        // Disable all failed
        mapper.AddStatic(HttpMethod.POST, baseUrl + "disable-failed", async ctx => {
            await DisableAllFailed();
            await ctx.SendNoContent();
        });

        // Parse (query: text, body: ProxyEndPointDefaults)
        mapper.AddStatic(HttpMethod.POST, baseUrl + "parse", async ctx => {
            var text = ctx.GetQueryParameter<string>("text");
            var defaults = ctx.ReadJson<ProxyEndPointDefaults>();
            var res = await Parse(text, defaults);
            await ctx.SendJson(res);
        });

        // Import (query: removeOld, body raw text list)
        mapper.AddStatic(HttpMethod.POST, baseUrl + "import", async ctx => {
            var text = ctx.ReadJson<string>();
            await Import(text);
            await ctx.SendNoContent();
        });

        // Reset state
        mapper.AddStatic(HttpMethod.POST, baseUrl + "reset-states", async ctx => {
            await ResetStates();
            await ctx.SendNoContent();
        });

        // Reload from URL
        mapper.AddStatic(HttpMethod.POST, baseUrl + "reload-url", async ctx => {
            await ReloadUrl(CancellationToken.None);
            await ctx.SendNoContent();
        });
    }

    public Task ResetStates()
    {
        ProxyEndPointService.ResetStates();
        return Task.CompletedTask;
    }

    public Task<AppProxyEndPointInfo?> GetDevice()
    {
        var result = ProxyEndPointService.GetDeviceProxy();
        return Task.FromResult(result);
    }

    public Task<AppProxyEndPointInfo[]> List(
        bool includeSucceeded = true,
        bool includeFailed = true,
        bool includeUnknown = true,
        bool includeDisabled = true,
        int? recordIndex = null, 
        int? recordCount = null)
    {
        var result = ProxyEndPointService.ListProxies(
            includeSucceeded: includeSucceeded,
            includeFailed: includeFailed,
            includeUnknown: includeUnknown,
            includeDisabled: includeDisabled,
            recordIndex: recordIndex,
            recordCount: recordCount);
        return Task.FromResult(result);
    }

    public Task<AppProxyEndPointInfo> Get(string proxyEndPointId, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var item = ProxyEndPointService.Get(proxyEndPointId);
        return Task.FromResult(item);
    }

    public Task<AppProxyEndPointInfo> Parse(string text, ProxyEndPointDefaults defaults)
    {
        var parsed = ProxyEndPointParser.ParseHostToUrl(text, defaults);
        var endpoint = ProxyEndPointParser.FromUrl(parsed);
        var info = new AppProxyEndPointInfo {
            EndPoint = endpoint,
            CountryCode = null
        };

        return Task.FromResult(info);
    }

    public Task<AppProxyEndPointInfo> Update(string proxyEndPointId, ProxyEndPoint proxyEndPoint)
    {
        var res = ProxyEndPointService.Update(proxyEndPointId, proxyEndPoint);
        return Task.FromResult(res);
    }

    public Task<AppProxyEndPointInfo> Add(ProxyEndPoint proxyEndPoint)
    {
        var res = ProxyEndPointService.Add(proxyEndPoint);
        return Task.FromResult(res);
    }

    public Task Delete(string proxyEndPointId)
    {
        ProxyEndPointService.Delete(proxyEndPointId);
        return Task.CompletedTask;
    }

    public Task DeleteAll(
        bool deleteSucceeded = true,
        bool deleteFailed = true,
        bool deleteUnknown = true,
        bool deleteDisabled = true)
    {
        ProxyEndPointService.DeleteAll(
            deleteSucceeded: deleteSucceeded, 
            deleteFailed: deleteFailed,
            deleteUnknown: deleteUnknown,
            deleteDisabled: deleteDisabled);
        return Task.CompletedTask;
    }

    public Task Import(string content)
    {
        ProxyEndPointService.Import(content);
        return Task.CompletedTask;
    }

    public Task DisableAllFailed()
    {
        ProxyEndPointService.DisableAllFailed();
        return Task.CompletedTask;
    }

    public Task ReloadUrl(CancellationToken cancellationToken)
    {
        return ProxyEndPointService.ReloadUrl(cancellationToken);
    }
}
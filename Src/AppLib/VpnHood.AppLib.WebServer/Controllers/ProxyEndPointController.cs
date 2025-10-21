using VpnHood.AppLib.Services.Proxies;
using VpnHood.AppLib.WebServer.Api;
using VpnHood.AppLib.WebServer.Extensions;
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
            var res = await List();
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
            await DeleteAll();
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

        // Reset states
        mapper.AddStatic(HttpMethod.POST, baseUrl + "reset-states", async ctx => {
            await ResetState();
            await ctx.SendNoContent();
        });
    }

    public Task ResetState()
    {
        ProxyEndPointService.ResetStates();
        return Task.CompletedTask;
    }

    public Task<AppProxyEndPointInfo?> GetDevice()
    {
        var result = ProxyEndPointService.GetDeviceProxy();
        return Task.FromResult(result);
    }
    
    public Task<AppProxyEndPointInfo[]> List()
    {
        var result = ProxyEndPointService.ListProxies();
        return Task.FromResult(result);
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

    public Task DeleteAll()
    {
        ProxyEndPointService.DeleteAll();
        return Task.CompletedTask;
    }

    public Task Import(string text)
    {
        ProxyEndPointService.Import(text);
        return Task.CompletedTask;
    }
}

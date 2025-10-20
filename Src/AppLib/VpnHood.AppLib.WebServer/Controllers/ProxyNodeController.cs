using VpnHood.AppLib.Services.Proxies;
using VpnHood.AppLib.WebServer.Api;
using VpnHood.AppLib.WebServer.Extensions;
using VpnHood.Core.Client.Abstractions.ProxyNodes;
using HttpMethod = WatsonWebserver.Core.HttpMethod;

namespace VpnHood.AppLib.WebServer.Controllers;

internal class ProxyNodeController : ControllerBase, IProxyNodeController
{
    private static AppProxyNodeService ProxyNodeService => VpnHoodApp.Instance.Services.ProxyNodeService;

    public override void AddRoutes(IRouteMapper mapper)
    {
        const string baseUrl = "/api/proxy-nodes/";

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
            var body = ctx.ReadJson<ProxyNode>();
            var res = await Add(body);
            await ctx.SendJson(res);
        });

        // Update
        mapper.AddParam(HttpMethod.PUT, baseUrl + "{proxyNodeId}", async ctx => {
            var id = ctx.GetRouteParameter<string>("proxyNodeId");
            var body = ctx.ReadJson<ProxyNode>();
            var res = await Update(id, body);
            await ctx.SendJson(res);
        });

        // Delete
        mapper.AddParam(HttpMethod.DELETE, baseUrl + "{proxyNodeId}", async ctx => {
            var id = ctx.GetRouteParameter<string>("proxyNodeId");
            await Delete(id);
            await ctx.SendNoContent();
        });

        // Delete all
        mapper.AddStatic(HttpMethod.DELETE, baseUrl, async ctx => {
            await DeleteAll();
            await ctx.SendNoContent();
        });

        // Parse (query: text, body: ProxyNodeDefaults)
        mapper.AddStatic(HttpMethod.POST, baseUrl + "parse", async ctx => {
            var text = ctx.GetQueryParameter<string>("text");
            var defaults = ctx.ReadJson<ProxyNodeDefaults>();
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
        ProxyNodeService.ResetStates();
        return Task.CompletedTask;
    }

    public Task<AppProxyNodeInfo?> GetDevice()
    {
        var result = ProxyNodeService.GetDeviceProxy();
        return Task.FromResult(result);
    }
    
    public Task<AppProxyNodeInfo[]> List()
    {
        var result = ProxyNodeService.ListProxies();
        return Task.FromResult(result);
    }

    public Task<AppProxyNodeInfo> Parse(string text, ProxyNodeDefaults defaults)
    {
        var parsed = ProxyNodeParser.ParseHostToUrl(text, defaults);
        var node = ProxyNodeParser.FromUrl(parsed);
        var info = new AppProxyNodeInfo {
            Node = node,
            CountryCode = null
        };

        return Task.FromResult(info);
    }

    public Task<AppProxyNodeInfo> Update(string proxyNodeId, ProxyNode proxyNode)
    {
        var res = ProxyNodeService.Update(proxyNodeId, proxyNode);
        return Task.FromResult(res);
    }

    public Task<AppProxyNodeInfo> Add(ProxyNode proxyNode)
    {
        var res = ProxyNodeService.Add(proxyNode);
        return Task.FromResult(res);
    }

    public Task Delete(string proxyNodeId)
    {
        ProxyNodeService.Delete(proxyNodeId);
        return Task.CompletedTask;
    }

    public Task DeleteAll()
    {
        ProxyNodeService.DeleteAll();
        return Task.CompletedTask;
    }

    public Task Import(string text)
    {
        ProxyNodeService.Import(text);
        return Task.CompletedTask;
    }
}

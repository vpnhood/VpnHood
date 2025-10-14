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

        // List
        mapper.AddStatic(HttpMethod.GET, baseUrl, async ctx => {
            var res = await List();
            await ctx.SendJson(res);
        });

        // Add
        mapper.AddStatic(HttpMethod.PUT, baseUrl, async ctx => {
            var body = ctx.ReadJson<ProxyNode>();
            var res = await Add(body);
            await ctx.SendJson(res);
        });

        // Update
        mapper.AddParam(HttpMethod.PATCH, baseUrl + "{proxyNodeId}", async ctx => {
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

        // Parse (query: text, body: ProxyNodeDefaults)
        mapper.AddStatic(HttpMethod.POST, baseUrl + "parse", async ctx => {
            var text = ctx.GetQueryParameter<string>("text");
            var defaults = ctx.ReadJson<ProxyNodeDefaults>();
            var res = await Parse(text, defaults);
            await ctx.SendJson(res);
        });

        // Import (query: removeOld, body raw text list)
        mapper.AddStatic(HttpMethod.POST, baseUrl + "import", async ctx => {
            var removeOld = ctx.GetQueryParameter<bool>("removeOld");
            var bytes = ctx.Request.DataAsBytes ?? [];
            var text = System.Text.Encoding.UTF8.GetString(bytes);
            await Import(text, removeOld);
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

    public Task<AppProxyNodeInfo[]> List()
    {
        var result = ProxyNodeService.GetNodeInfos();
        return Task.FromResult(result);
    }

    public Task<AppProxyNodeInfo> Parse(string text, ProxyNodeDefaults defaults)
    {
        if (!ProxyNodeParser.TryParseToUrl(text, defaults, out var parsed))
            throw new ArgumentException("Invalid proxy url.");

        var node = ProxyNodeParser.FromUrl(parsed);
        var info = new AppProxyNodeInfo(node) {
            CountryCode = null
        };

        return Task.FromResult(info);
    }

    public Task<AppProxyNodeInfo> Update(string proxyNodeId, ProxyNode proxyNode)
    {
        return ProxyNodeService.Update(proxyNodeId, proxyNode, true);
    }

    public Task<AppProxyNodeInfo> Add(ProxyNode proxyNode)
    {
        return ProxyNodeService.Add(proxyNode);
    }

    public Task Delete(string proxyNodeId)
    {
        return ProxyNodeService.Delete(proxyNodeId);
    }

    public Task Import(string text, bool removeOld)
    {
        return ProxyNodeService.Import(text, removeOld);
    }
}

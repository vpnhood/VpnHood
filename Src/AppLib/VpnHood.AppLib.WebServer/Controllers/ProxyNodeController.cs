using VpnHood.AppLib.Services.Proxies;
using VpnHood.AppLib.WebServer.Api;
using VpnHood.AppLib.WebServer.Extensions;
using VpnHood.Core.Client.Abstractions.ProxyNodes;
using HttpMethod = WatsonWebserver.Core.HttpMethod;

namespace VpnHood.AppLib.WebServer.Controllers;

internal class ProxyNodeController : ControllerBase, IProxyNodeController
{
    private static VpnHoodApp App => VpnHoodApp.Instance;

    public override void AddRoutes(IRouteMapper mapper)
    {
        const string baseUrl = "/api/proxy-nodes/";

        mapper.AddStatic(HttpMethod.GET, baseUrl, async ctx => {
            var res = await List();
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "parse", async ctx => {
            var url = ctx.GetQueryParameter<string>("url");
            var defaults = ctx.ReadJson<ProxyNodeDefaults>();
            var res = await Parse(url, defaults);
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl, async ctx => {
            var body = ctx.ReadJson<ProxyNode>();
            var res = await Add(body);
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.PATCH, baseUrl, async ctx => {
            var url = ctx.GetQueryParameter<Uri>("url");
            var body = ctx.ReadJson<ProxyNode>();
            var res = await Update(url, body);
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.DELETE, baseUrl, async ctx => {
            var url = ctx.GetQueryParameter<Uri>("url");
            await Delete(url);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "import", async ctx => {
            var removeOld = ctx.GetQueryParameter<bool>("removeOld");
            var text = ctx.Request.DataAsString;
            await Import(text, removeOld);
            await ctx.SendNoContent();
        });
    }

    public Task<AppProxyNodeInfo[]> List()
    {
        var result = App.Services.ProxyNodeService.GetNodeInfos();
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

    public Task<AppProxyNodeInfo> Update(Uri url, ProxyNode proxyNode)
    {
        return App.Services.ProxyNodeService.Update(url, proxyNode, true);
    }

    public Task<AppProxyNodeInfo> Add(ProxyNode proxyNode)
    {
        return App.Services.ProxyNodeService.Add(proxyNode);
    }

    public Task Delete(Uri url)
    {
        return App.Services.ProxyNodeService.Delete(url);
    }

    public Task Import(string text, bool removeOld)
    {
        return App.Services.ProxyNodeService.Import(text, removeOld);
    }
}
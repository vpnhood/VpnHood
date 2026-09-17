using VpnHood.AppLib.Api.WebHost.Helpers;
using HttpMethod = WatsonWebserver.Core.HttpMethod;

using VpnHood.AppLib.Contracts.Proxies;

namespace VpnHood.AppLib.Api.WebHost.Controllers;

internal class ProxyEndPointController(IProxyEndPointsApi api) : ControllerBase
{
    public override void AddRoutes(IRouteMapper mapper)
    {
        const string baseUrl = "/api/proxy-endpoints/";

        // Get device proxy
        mapper.AddStatic(HttpMethod.GET, baseUrl + "device", async ctx => {
            var res = await api.GetDevice(ctx.Token);
            await ctx.SendJson(res);
        });

        // List
        mapper.AddStatic(HttpMethod.GET, baseUrl, async ctx => {
            var res = await api.List(
                search: ctx.GetQueryParameter<string?>("search", null),
                includeSucceeded: ctx.GetQueryParameter("includeSucceeded", true),
                includeFailed: ctx.GetQueryParameter("includeFailed", true),
                includeUnknown: ctx.GetQueryParameter("includeUnknown", true),
                includeDisabled: ctx.GetQueryParameter("includeDisabled", true),
                recordIndex: ctx.GetQueryParameter<int?>("recordIndex", null),
                recordCount: ctx.GetQueryParameter<int?>("recordCount", null),
                cancellationToken: ctx.Token);
            await ctx.SendJson(res);
        });

        // Get by id
        mapper.AddParam(HttpMethod.GET, baseUrl + "{proxyEndPointId}", async ctx => {
            var id = ctx.GetRouteParameter<string>("proxyEndPointId");
            var res = await api.Get(id, ctx.Token);
            await ctx.SendJson(res);
        });

        // Add
        mapper.AddStatic(HttpMethod.POST, baseUrl, async ctx => {
            var body = ctx.ReadJson<ProxyEndPoint>();
            var res = await api.Add(body, ctx.Token);
            await ctx.SendJson(res);
        });

        // Update
        mapper.AddParam(HttpMethod.PUT, baseUrl + "{proxyEndPointId}", async ctx => {
            var id = ctx.GetRouteParameter<string>("proxyEndPointId");
            var body = ctx.ReadJson<ProxyEndPoint>();
            var res = await api.Update(id, body, ctx.Token);
            await ctx.SendJson(res);
        });

        // Delete
        mapper.AddParam(HttpMethod.DELETE, baseUrl + "{proxyEndPointId}", async ctx => {
            var id = ctx.GetRouteParameter<string>("proxyEndPointId");
            await api.Delete(id, ctx.Token);
            await ctx.SendNoContent();
        });

        // Delete all
        mapper.AddStatic(HttpMethod.DELETE, baseUrl, async ctx => {
            await api.DeleteAll(
                deleteSucceeded: ctx.GetQueryParameter("deleteSucceeded", true),
                deleteFailed: ctx.GetQueryParameter("deleteFailed", true),
                deleteUnknown: ctx.GetQueryParameter("deleteUnknown", true),
                deleteDisabled: ctx.GetQueryParameter("deleteDisabled", true),
                cancellationToken: ctx.Token);

            await ctx.SendNoContent();
        });

        // Disable all failed
        mapper.AddStatic(HttpMethod.POST, baseUrl + "disable-failed", async ctx => {
            await api.DisableAllFailed(ctx.Token);
            await ctx.SendNoContent();
        });

        // Parse (query: text, body: ProxyEndPointDefaults)
        mapper.AddStatic(HttpMethod.POST, baseUrl + "parse", async ctx => {
            var text = ctx.GetQueryParameter<string>("text");
            var defaults = ctx.ReadJson<ProxyEndPointDefaults>();
            var res = await api.Parse(text, defaults, ctx.Token);
            await ctx.SendJson(res);
        });

        // Import (body raw text list)
        mapper.AddStatic(HttpMethod.POST, baseUrl + "import", async ctx => {
            var text = ctx.ReadJson<string>();
            await api.Import(text, ctx.Token);
            await ctx.SendNoContent();
        });

        // Reset state
        mapper.AddStatic(HttpMethod.POST, baseUrl + "reset-states", async ctx => {
            await api.ResetStates(ctx.Token);
            await ctx.SendNoContent();
        });

        // Reload from URL
        mapper.AddStatic(HttpMethod.POST, baseUrl + "reload-url", async ctx => {
            await api.ReloadUrl(ctx.Token);
            await ctx.SendNoContent();
        });
    }
}

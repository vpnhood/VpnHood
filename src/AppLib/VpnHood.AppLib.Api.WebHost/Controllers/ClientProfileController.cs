using VpnHood.AppLib.Api.WebHost.Helpers;
using HttpMethod = WatsonWebserver.Core.HttpMethod;
using VpnHood.AppLib.Api.ClientProfiles;

namespace VpnHood.AppLib.Api.WebHost.Controllers;

internal class ClientProfileController(IClientProfilesApi api) : ControllerBase
{
    public override void AddRoutes(IRouteMapper mapper)
    {
        const string baseUrl = "/api/client-profiles/";

        mapper.AddStatic(HttpMethod.PUT, baseUrl + "access-keys", async ctx => {
            var accessKey = ctx.GetQueryParameter<string>("accessKey");
            var res = await api.AddByAccessKey(accessKey, ctx.Token);
            await ctx.SendJson(res);
        });

        mapper.AddParam(HttpMethod.GET, baseUrl + "{id}", async ctx => {
            var id = ctx.GetRouteParameter<Guid>("id");
            var res = await api.Get(id, ctx.Token);
            await ctx.SendJson(res);
        });

        mapper.AddParam(HttpMethod.GET, baseUrl + "{id}/access-code", async ctx => {
            var id = ctx.GetRouteParameter<Guid>("id");
            var res = await api.GetAccessCode(id, ctx.Token);
            await ctx.SendJson(res);
        });

        mapper.AddParam(HttpMethod.PATCH, baseUrl + "{id}", async ctx => {
            var id = ctx.GetRouteParameter<Guid>("id");
            var body = ctx.ReadJson<ClientProfileUpdateParams>();
            var res = await api.Update(id, body, ctx.Token);
            await ctx.SendJson(res);
        });

        mapper.AddParam(HttpMethod.DELETE, baseUrl + "{id}", async ctx => {
            var id = ctx.GetRouteParameter<Guid>("id");
            await api.Delete(id, ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddParam(HttpMethod.GET, baseUrl + "{id}/purchase-options", async ctx => {
            var id = ctx.GetRouteParameter<Guid>("id");
            var res = await api.GetPurchaseOptions(id, ctx.Token);
            await ctx.SendJson(res);
        });
    }
}

using VpnHood.AppLib.Api.WebHost.Helpers;
using HttpMethod = WatsonWebserver.Core.HttpMethod;
using VpnHood.AppLib.Api.VpnProfiles;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppLib.Api.WebHost.Controllers;

internal class VpnProfileController(IVpnProfilesApi api) : ControllerBase
{
    public override void AddRoutes(IRouteMapper mapper)
    {
        const string baseUrl = "/api/vpn-profiles/";

        mapper.AddStatic(HttpMethod.PUT, baseUrl + "access-keys", async ctx => {
            var accessKey = ctx.GetQueryParameter<string>("accessKey");
            var res = await api.AddByAccessKey(accessKey, ctx.Token).Vhc();
            await ctx.SendJson(res).Vhc();
        });

        mapper.AddParam(HttpMethod.GET, baseUrl + "{id}", async ctx => {
            var id = ctx.GetRouteParameter<Guid>("id");
            var res = await api.Get(id, ctx.Token).Vhc();
            await ctx.SendJson(res).Vhc();
        });

        mapper.AddParam(HttpMethod.GET, baseUrl + "{id}/access-code", async ctx => {
            var id = ctx.GetRouteParameter<Guid>("id");
            var res = await api.GetAccessCode(id, ctx.Token).Vhc();
            await ctx.SendJson(res).Vhc();
        });

        mapper.AddParam(HttpMethod.PATCH, baseUrl + "{id}", async ctx => {
            var id = ctx.GetRouteParameter<Guid>("id");
            var body = ctx.ReadJson<VpnProfileUpdateParams>();
            var res = await api.Update(id, body, ctx.Token).Vhc();
            await ctx.SendJson(res).Vhc();
        });

        mapper.AddParam(HttpMethod.DELETE, baseUrl + "{id}", async ctx => {
            var id = ctx.GetRouteParameter<Guid>("id");
            await api.Delete(id, ctx.Token).Vhc();
            await ctx.SendNoContent().Vhc();
        });

        mapper.AddParam(HttpMethod.GET, baseUrl + "{id}/purchase-options", async ctx => {
            var id = ctx.GetRouteParameter<Guid>("id");
            var res = await api.GetPurchaseOptions(id, ctx.Token).Vhc();
            await ctx.SendJson(res).Vhc();
        });
    }
}

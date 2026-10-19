using VpnHood.AppLib.Api.WebHost.Helpers;
using HttpMethod = WatsonWebserver.Core.HttpMethod;
using VpnHood.AppLib.Api.Accounts;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppLib.Api.WebHost.Controllers;

internal class AccountController(IAccountApi api) : ControllerBase
{
    public override void AddRoutes(IRouteMapper mapper)
    {
        const string baseUrl = "/api/account/";

        mapper.AddStatic(HttpMethod.GET, baseUrl, async ctx => {
            var res = await api.Get(ctx.Token).Vhc();
            await ctx.SendJson(res).Vhc();
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "password-reset-url", async ctx => {
            var res = await api.GetPasswordResetUrl(ctx.Token).Vhc();
            await ctx.SendJson(res).Vhc();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "refresh", async ctx => {
            await api.Refresh(ctx.Token).Vhc();
            await ctx.SendNoContent().Vhc();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "sign-in", async ctx => {
            var signInOptions = ctx.ReadJson<SignInOptions>();
            var res = await api.SignIn(signInOptions, ctx.Token).Vhc();
            await ctx.SendJson(res).Vhc();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "sign-out", async ctx => {
            await api.SignOut(ctx.Token).Vhc();
            await ctx.SendNoContent().Vhc();
        });

        mapper.AddStatic(HttpMethod.DELETE, baseUrl, async ctx => {
            await api.Delete(ctx.Token).Vhc();
            await ctx.SendNoContent().Vhc();
        });
    }
}

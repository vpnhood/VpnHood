using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Services.Accounts;
using VpnHood.AppLib.WebServer.Api;
using VpnHood.AppLib.WebServer.Extensions;
using VpnHood.Core.Client.Device.UiContexts;
using HttpMethod = WatsonWebserver.Core.HttpMethod;

namespace VpnHood.AppLib.WebServer.Controllers;

internal class AccountController : ControllerBase, IAccountController
{
    private static AppAccountService AccountService =>
        VpnHoodApp.Instance.Services.AccountService ??
        throw new Exception("Account service is not available at this moment.");

    public override void AddRoutes(IRouteMapper mapper)
    {
        const string baseUrl = "/api/account/";

        mapper.AddStatic(HttpMethod.GET, baseUrl, async ctx => {
            var res = await Get();
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "refresh", async ctx => {
            await Refresh();
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "is-signin-with-google-supported", async ctx => {
            var res = IsSigninWithGoogleSupported();
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "signin-with-google", async ctx => {
            await SignInWithGoogle();
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "sign-out", async ctx => {
            await SignOut();
            await ctx.SendNoContent();
        });

        mapper.AddParam(HttpMethod.GET, baseUrl + "subscriptions/{subId}/access-keys", async ctx => {
            var subId = ctx.GetRouteParameter<string>("subId");
            var res = await ListAccessKeys(subId);
            await ctx.SendJson(res);
        });
    }

    public Task<AppAccount?> Get()
    {
        return VpnHoodApp.Instance.Services.AccountService != null
            ? VpnHoodApp.Instance.Services.AccountService.GetAccount()
            : Task.FromResult<AppAccount?>(null);
    }

    public Task Refresh()
    {
        return AccountService.Refresh();
    }

    public bool IsSigninWithGoogleSupported()
    {
        return VpnHoodApp.Instance.Services.AccountService?.AuthenticationService.IsSignInWithGoogleSupported ?? false;
    }

    public Task SignInWithGoogle()
    {
        if (!AccountService.AuthenticationService.IsSignInWithGoogleSupported)
            throw new NotSupportedException("Sign in with Google is not supported.");

        return AccountService.AuthenticationService.SignInWithGoogle(AppUiContext.RequiredContext);
    }

    public Task SignOut()
    {
        return AccountService.AuthenticationService.SignOut(AppUiContext.RequiredContext);
    }

    public Task<string[]> ListAccessKeys(string subscriptionId)
    {
        return AccountService.ListAccessKeys(subscriptionId);
    }
}
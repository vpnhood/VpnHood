using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Services.Accounts;
using VpnHood.AppLib.WebServer.Api;
using VpnHood.AppLib.WebServer.Helpers;
using VpnHood.Core.Client.Devices.UiContexts;
using HttpMethod = WatsonWebserver.Core.HttpMethod;

namespace VpnHood.AppLib.WebServer.Controllers;

internal class AccountController(VpnHoodApp app) : ControllerBase, IAccountController
{
    private AppAccountService AccountService =>
        app.Services.AccountService ??
        throw new Exception("Account service is not available at this moment.");

    public override void AddRoutes(IRouteMapper mapper)
    {
        const string baseUrl = "/api/account/";

        mapper.AddStatic(HttpMethod.GET, baseUrl, async ctx => {
            var res = await Get(ctx.Token);
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "refresh", async ctx => {
            await Refresh(ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "sign-in", async ctx => {
            var signInOptions = ctx.ReadJson<AppSignInOptions>();
            await SignIn(signInOptions, ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "sign-out", async ctx => {
            await SignOut(ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddParam(HttpMethod.GET, baseUrl + "subscriptions/{subId}/access-keys", async ctx => {
            var subId = ctx.GetRouteParameter<string>("subId");
            var res = await ListAccessKeys(subId, ctx.Token);
            await ctx.SendJson(res);
        });
    }

    public Task<AppAccount?> Get(CancellationToken cancellationToken)
    {
        return app.Services.AccountService != null
            ? app.Services.AccountService.GetAccount(cancellationToken)
            : Task.FromResult<AppAccount?>(null);
    }

    public Task Refresh(CancellationToken cancellationToken)
    {
        return AccountService.Refresh(cancellationToken: cancellationToken);
    }

    public Task SignIn(AppSignInOptions signInOptions, CancellationToken cancellationToken)
    {
        if (!AccountService.AuthenticationService.SignInMethods.Contains(signInOptions.Method))
            throw new NotSupportedException($"Sign-in method is not supported. Method: {signInOptions.Method}");

        return AccountService.AuthenticationService.SignIn(AppUiContext.RequiredContext, signInOptions,
            cancellationToken);
    }

    public Task SignOut(CancellationToken cancellationToken)
    {
        return AccountService.AuthenticationService.SignOut(AppUiContext.RequiredContext, cancellationToken);
    }

    public Task<IReadOnlyList<string>> ListAccessKeys(string subscriptionId, CancellationToken cancellationToken)
    {
        return AccountService.ListAccessKeys(subscriptionId, cancellationToken);
    }
}
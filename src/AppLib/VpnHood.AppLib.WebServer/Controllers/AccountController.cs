using HttpMethod = WatsonWebserver.Core.HttpMethod;
using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.AppLib.Services.Accounts;
using VpnHood.AppLib.WebServer.Api;
using VpnHood.AppLib.WebServer.Helpers;
using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.AppLib.WebServer.Controllers;

internal class AccountController(VpnHoodApp app) : ControllerBase, IAccountController
{
    private AccountService AccountService =>
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
            var signInOptions = ctx.ReadJson<SignInOptions>();
            var res = await SignIn(signInOptions, ctx.Token);
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "sign-out", async ctx => {
            await SignOut(ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.DELETE, baseUrl, async ctx => {
            await Delete(ctx.Token);
            await ctx.SendNoContent();
        });
    }

    public Task<Account?> Get(CancellationToken cancellationToken)
    {
        return app.Services.AccountService != null
            ? app.Services.AccountService.GetAccount(cancellationToken)
            : Task.FromResult<Account?>(null);
    }

    public Task Refresh(CancellationToken cancellationToken)
    {
        return AccountService.Refresh(cancellationToken: cancellationToken);
    }

    public Task<SignInResult> SignIn(SignInOptions signInOptions, CancellationToken cancellationToken)
    {
        if (!AccountService.AuthenticationService.ProviderIds.Contains(signInOptions.ProviderId))
            throw new NotSupportedException($"Sign-in provider is not supported. ProviderId: {signInOptions.ProviderId}");

        return AccountService.AuthenticationService.SignIn(AppUiContext.RequiredContext, signInOptions,
            cancellationToken);
    }

    public Task SignOut(CancellationToken cancellationToken)
    {
        return AccountService.AuthenticationService.SignOut(AppUiContext.RequiredContext, cancellationToken);
    }

    public Task Delete(CancellationToken cancellationToken)
    {
        return AccountService.DeleteAccount(AppUiContext.RequiredContext, cancellationToken);
    }
}
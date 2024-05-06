using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using VpnHood.Client.App.Abstractions;
using VpnHood.Client.App.WebServer.Api;

namespace VpnHood.Client.App.WebServer.Controllers;

internal class AccountController : WebApiController, IAccountController
{
    public IAppAccountService AccountService => VpnHoodApp.Instance.Services.AccountService
        ?? throw new Exception("Account service is not available at this moment.");

    [Route(HttpVerbs.Get, "/")]
    public Task<AppAccount?> Get()
    {
        return AccountService.GetAccount();
    }

    [Route(HttpVerbs.Post, "/refresh")]
    public Task Refresh()
    {
        return VpnHoodApp.Instance.RefreshAccount();
    }

    [Route(HttpVerbs.Get, "/is-signin-with-google-supported")]
    public bool IsSigninWithGoogleSupported()
    {
        return VpnHoodApp.Instance.Services.AccountService?.Authentication.IsSignInWithGoogleSupported ?? false;
    }

    [Route(HttpVerbs.Post, "/signin-with-google")]
    public Task SignInWithGoogle()
    {
        if (!AccountService.Authentication.IsSignInWithGoogleSupported)
            throw new NotSupportedException("Sign in with Google is not supported.");

        return AccountService.Authentication.SignInWithGoogle(VpnHoodApp.Instance.RequiredUiContext);
    }

    [Route(HttpVerbs.Post, "/sign-out")]
    public async Task SignOut()
    {
        await AccountService.Authentication.SignOut(VpnHoodApp.Instance.RequiredUiContext);
        await VpnHoodApp.Instance.RefreshAccount();
    }

    [Route(HttpVerbs.Get, "/subscription-orders/providerOrderId:{providerOrderId}/is-processed")]
    public Task<bool> IsSubscriptionOrderProcessed(string providerOrderId)
    {
        return AccountService.IsSubscriptionOrderProcessed(providerOrderId);
    }

    [Route(HttpVerbs.Get, "/subscriptions/{subscriptionId}/access-keys")]
    public Task<string[]> GetAccessKeys(string subscriptionId)
    {
        return AccountService.GetAccessKeys(subscriptionId);
    }
}
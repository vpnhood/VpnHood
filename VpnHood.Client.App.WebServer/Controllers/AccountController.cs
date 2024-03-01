using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using VpnHood.Client.App.Abstractions;
using VpnHood.Client.App.WebServer.Api;

namespace VpnHood.Client.App.WebServer.Controllers;

internal class AccountController : WebApiController, IAccountController
{
    public IAppAccountService AccountService => VpnHoodApp.Instance.AccountService
        ?? throw new Exception("Account service is not available at this moment.");

    [Route(HttpVerbs.Get, "/")]
    public Task<AppAccount?> Get()
    {
        return Task.FromResult((AppAccount?)null);
        return AccountService.GetAccount();
    }

    [Route(HttpVerbs.Get, "/is-signin-with-google-supported")]
    public bool IsSigninWithGoogleSupported()
    {
        return VpnHoodApp.Instance.AccountService?.Authentication.IsSignInWithGoogleSupported ?? false;
    }

    [Route(HttpVerbs.Post, "/signin-with-google")]
    public Task SignInWithGoogle()
    {
        if (!AccountService.Authentication.IsSignInWithGoogleSupported)
            throw new NotSupportedException("Sign in with Google is not supported.");

        return AccountService.Authentication.SignInWithGoogle();
    }

    [Route(HttpVerbs.Post, "/sign-out")]
    public Task SignOut()
    {
        return AccountService.Authentication.SignOut();
    }

    [Route(HttpVerbs.Get, "/subscription-orders/providerOrderId:{providerOrderId}/is-processed")]
    public Task<bool> IsSubscriptionOrderProcessed(string providerOrderId)
    {
        return AccountService.IsSubscriptionOrderProcessed(providerOrderId);
    }

    [Route(HttpVerbs.Get, "/subscriptions/{subscriptionId}/access-keys")]
    public Task<List<string>> GetAccessKeys(string subscriptionId)
    {
        return AccountService.GetAccessKeys(subscriptionId);
    }
}
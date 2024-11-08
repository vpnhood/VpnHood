using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using VpnHood.Client.App.Abstractions;
using VpnHood.Client.App.Services.Accounts;
using VpnHood.Client.App.WebServer.Api;
using VpnHood.Client.Device;

namespace VpnHood.Client.App.WebServer.Controllers;

internal class AccountController : WebApiController, IAccountController
{
    private static AppAccountService AccountService => 
        VpnHoodApp.Instance.Services.AccountService ?? throw new Exception("Account service is not available at this moment.");

    [Route(HttpVerbs.Get, "/")]
    public Task<AppAccount?> Get()
    {
        return AccountService.GetAccount();
    }

    [Route(HttpVerbs.Post, "/refresh")]
    public Task Refresh()
    {
        return AccountService.Refresh();
    }

    [Route(HttpVerbs.Get, "/is-signin-with-google-supported")]
    public bool IsSigninWithGoogleSupported()
    {
        return VpnHoodApp.Instance.Services.AccountService?.AuthenticationService.IsSignInWithGoogleSupported ?? false;
    }

    [Route(HttpVerbs.Post, "/signin-with-google")]
    public Task SignInWithGoogle()
    {
        if (!AccountService.AuthenticationService.IsSignInWithGoogleSupported)
            throw new NotSupportedException("Sign in with Google is not supported.");

        return AccountService.AuthenticationService.SignInWithGoogle(ActiveUiContext.RequiredContext);
    }

    [Route(HttpVerbs.Post, "/sign-out")]
    public Task SignOut()
    {
        return AccountService.AuthenticationService.SignOut(ActiveUiContext.RequiredContext);
    }

    [Route(HttpVerbs.Get, "/subscriptions/{subscriptionId}/access-keys")]
    public Task<string[]> GetAccessKeys(string subscriptionId)
    {
        return AccountService.GetAccessKeys(subscriptionId);
    }
}
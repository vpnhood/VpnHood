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

    [Route(HttpVerbs.Get, "/is-signin-with-google-supported")]
    public bool IsSigninWithGoogleSupported()
    {
        return VpnHoodApp.Instance.AccountService?.IsGoogleSignInSupported ?? false;
    }

    [Route(HttpVerbs.Post, "/signin-with-google")]
    public Task SignInWithGoogle()
    {
        if (!AccountService.IsGoogleSignInSupported)
            throw new NotSupportedException("Sign in with Google is not supported.");

        return AccountService.SignInWithGoogle();
    }

    [Route(HttpVerbs.Post, "/sign-out")]
    public Task SignOut()
    {
        return AccountService.SignOut();
    }

    [Route(HttpVerbs.Get, "/")]
    public Task<AppAccount> Get()
    {
        return AccountService.GetAccount();
    }
}
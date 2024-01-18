using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using VpnHood.Client.App.Accounts;
using VpnHood.Client.App.WebServer.Api;

namespace VpnHood.Client.App.WebServer.Controllers;

internal class AccountController : WebApiController, IAccountController
{
    public IAccountService AccountService => VpnHoodApp.Instance.AccountService
        ?? throw new Exception("Account service is not available at this moment.");

    [Route(HttpVerbs.Get, "/is-signin-with-google-supported")]
    public bool IsSigninWithGoogleSupported()
    {
        return AccountService.IsGoogleSignInSupported;
    }

    [Route(HttpVerbs.Post, "/signin-with-google")]
    public Task SignInWithGoogle()
    {
        if (!AccountService.IsGoogleSignInSupported)
            throw new NotSupportedException("Sign in with Google is not supported.");

        return AccountService.SignInWithGoogle();
    }

    [Route(HttpVerbs.Get, "/account")]
    public Task<Account> GetAccount()
    {
        return AccountService.GetAccount();
    }
}
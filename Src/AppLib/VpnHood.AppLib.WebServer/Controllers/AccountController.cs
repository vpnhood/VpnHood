using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Services.Accounts;
using VpnHood.AppLib.WebServer.Api;
using VpnHood.Core.Client.Device.UiContexts;

namespace VpnHood.AppLib.WebServer.Controllers;

internal class AccountController : IAccountController
{
    private static AppAccountService AccountService =>
        VpnHoodApp.Instance.Services.AccountService ??
        throw new Exception("Account service is not available at this moment.");

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
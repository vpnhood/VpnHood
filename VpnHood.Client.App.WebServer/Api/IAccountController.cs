using VpnHood.Client.App.Accounts;

namespace VpnHood.Client.App.WebServer.Api;

internal interface IAccountController
{
    bool IsSigninWithGoogleSupported();
    Task SignInWithGoogle();
    Task<Account> GetAccount();
}
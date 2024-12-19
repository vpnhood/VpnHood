using VpnHood.AppLib.Abstractions;

namespace VpnHood.AppLib.WebServer.Api;

public interface IAccountController
{
    bool IsSigninWithGoogleSupported();
    Task SignInWithGoogle();
    Task SignOut();
    Task Refresh();
    Task<AppAccount?> Get();
    Task<string[]> ListAccessKeys(string subscriptionId);
}
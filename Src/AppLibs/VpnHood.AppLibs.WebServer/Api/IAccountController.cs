using VpnHood.AppLibs.Abstractions;

namespace VpnHood.AppLibs.WebServer.Api;

public interface IAccountController
{
    bool IsSigninWithGoogleSupported();
    Task SignInWithGoogle();
    Task SignOut();
    Task Refresh();
    Task<AppAccount?> Get();
    Task<string[]> GetAccessKeys(string subscriptionId);
}
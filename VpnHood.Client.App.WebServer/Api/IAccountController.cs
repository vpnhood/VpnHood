using VpnHood.Client.App.Abstractions;

namespace VpnHood.Client.App.WebServer.Api;

public interface IAccountController
{
    bool IsSigninWithGoogleSupported();
    Task SignInWithGoogle();
    Task SignOut();
    Task Refresh();
    Task<AppAccount?> Get();
    Task<string[]> GetAccessKeys(string subscriptionId);
}
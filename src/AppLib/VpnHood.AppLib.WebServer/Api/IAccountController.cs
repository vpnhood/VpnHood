using VpnHood.AppLib.Abstractions;

namespace VpnHood.AppLib.WebServer.Api;

public interface IAccountController
{
    bool IsSigninWithGoogleSupported();
    Task SignInWithGoogle(CancellationToken cancellationToken);
    Task SignOut(CancellationToken cancellationToken);
    Task Refresh(CancellationToken cancellationToken);
    Task<AppAccount?> Get(CancellationToken cancellationToken);
    Task<string[]> ListAccessKeys(string subscriptionId, CancellationToken cancellationToken);
}
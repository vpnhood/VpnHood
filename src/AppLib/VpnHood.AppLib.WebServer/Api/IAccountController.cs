using VpnHood.AppLib.Abstractions;

namespace VpnHood.AppLib.WebServer.Api;

public interface IAccountController
{
    Task SignIn(AppSignInOptions signInOptions, CancellationToken cancellationToken);
    Task SignOut(CancellationToken cancellationToken);
    Task Refresh(CancellationToken cancellationToken);
    Task<AppAccount?> Get(CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ListAccessKeys(string subscriptionId, CancellationToken cancellationToken);
}

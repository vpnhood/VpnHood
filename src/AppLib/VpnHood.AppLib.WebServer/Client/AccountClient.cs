using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.AppLib.WebServer.Api;

namespace VpnHood.AppLib.WebServer.Client;

// IAccountController over HTTP: the routes of AccountController, one for one.
internal sealed class AccountClient(HttpClient httpClient) : AppApiClientBase(httpClient), IAccountController
{
    private const string BaseUrl = "api/account/";

    public Task<SignInResult> SignIn(SignInOptions signInOptions, CancellationToken cancellationToken)
    {
        return PostAsync<SignInOptions, SignInResult>(BaseUrl + "sign-in", null, signInOptions, cancellationToken);
    }

    public Task SignOut(CancellationToken cancellationToken)
    {
        return PostAsync(BaseUrl + "sign-out", null, cancellationToken);
    }

    public Task Delete(CancellationToken cancellationToken)
    {
        return DeleteAsync(BaseUrl, null, cancellationToken);
    }

    public Task Refresh(CancellationToken cancellationToken)
    {
        return PostAsync(BaseUrl + "refresh", null, cancellationToken);
    }

    // No account is an empty reply, not a failure.
    public Task<Account?> Get(CancellationToken cancellationToken)
    {
        return GetOrDefaultAsync<Account>(BaseUrl, cancellationToken);
    }
}

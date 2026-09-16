using VpnHood.AppLib.Abstractions.Accounts;

namespace VpnHood.AppLib.Api.Clients;

// IAccountApi over HTTP: the routes of AccountController, one for one.
internal sealed class AccountClient(HttpClient httpClient) : AppApiClientBase(httpClient), IAccountApi
{
    private const string BaseUrl = "api/account/";

    public Task<SignInResult> SignIn(SignInOptions signInOptions, CancellationToken cancellationToken)
    {
        return HttpPostAsync<SignInResult>(BaseUrl + "sign-in", null, signInOptions, cancellationToken);
    }

    public Task SignOut(CancellationToken cancellationToken)
    {
        return HttpPostAsync(BaseUrl + "sign-out", null, null, cancellationToken);
    }

    public Task Delete(CancellationToken cancellationToken)
    {
        return HttpDeleteAsync(BaseUrl, null, cancellationToken);
    }

    public Task Refresh(CancellationToken cancellationToken)
    {
        return HttpPostAsync(BaseUrl + "refresh", null, null, cancellationToken);
    }

    // No account is an empty reply, not a failure.
    public Task<Account?> Get(CancellationToken cancellationToken)
    {
        return HttpGetAsync<Account?>(BaseUrl, null, cancellationToken);
    }
}

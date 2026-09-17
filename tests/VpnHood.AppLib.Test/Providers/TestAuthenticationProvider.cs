using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.AppLib.Test.Providers;

internal class TestAuthenticationProvider : IAuthenticationProvider
{
    public IReadOnlyList<string> ProviderIds => [AuthProviders.Google];
    public Uri? AccountWebsiteUrl => null;
    public string? UserId { get; private set; }

    public Task<string?> GetAccessToken(CancellationToken cancellationToken)
    {
        return Task.FromResult(UserId == null ? null : "test-access-token");
    }

    public void InvalidateAccessToken(string accessToken)
    {
        UserId = null;
    }

    public Task<SignInResult> SignIn(IUiContext uiContext, SignInOptions signInOptions,
        CancellationToken cancellationToken)
    {
        UserId = Guid.Empty.ToString();
        return Task.FromResult(new SignInResult { State = SignInState.SignedIn });
    }

    public Task SignOut(IUiContext uiContext, CancellationToken cancellationToken)
    {
        UserId = null;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }
}

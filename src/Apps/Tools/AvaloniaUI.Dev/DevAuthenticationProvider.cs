using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;

namespace VpnHood.App.AvaloniaUI.Dev;

// A store build's sign-in without the store or the portal: the store's own provider and a password,
// as the portal offers them, and any of them signs a sample person in at once.
internal sealed class DevAuthenticationProvider(string storeAuthProviderId) : IAuthenticationProvider
{
    public IReadOnlyList<string> ProviderIds { get; } = [storeAuthProviderId, AuthProviders.Password];
    public Uri? AccountWebsiteUrl { get; } = new("https://account.example.com/");
    public string? UserId { get; private set; }

    public Task<string?> GetAccessToken(CancellationToken cancellationToken)
    {
        return Task.FromResult(UserId == null ? null : "dev-access-token");
    }

    public void InvalidateAccessToken(string accessToken)
    {
        UserId = null;
    }

    public Task<SignInResult> SignIn(IUiContext uiContext, SignInOptions signInOptions, CancellationToken cancellationToken)
    {
        UserId = "dev-user";
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

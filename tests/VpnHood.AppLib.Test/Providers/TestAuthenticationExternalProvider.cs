using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.AppLib.Test.Providers;

/// <summary>The platform sign-in double: hands out a fixed id token.</summary>
internal class TestAuthenticationExternalProvider(string idToken,
    string providerId = AuthProviders.Google) : IAuthenticationExternalProvider
{
    public string ProviderId => providerId;
    public int SignInCalls { get; private set; }
    public int SignOutCalls { get; private set; }

    public Task<string> SignIn(IUiContext uiContext, bool isSilentLogin, CancellationToken cancellationToken)
    {
        SignInCalls++;
        return Task.FromResult(idToken);
    }

    public Task SignOut(IUiContext uiContext, CancellationToken cancellationToken)
    {
        SignOutCalls++;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }
}

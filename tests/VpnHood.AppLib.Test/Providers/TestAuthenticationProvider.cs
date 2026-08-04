using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.AppLib.Test.Providers;

internal class TestAuthenticationProvider : IAppAuthenticationProvider
{
    public IReadOnlyList<AppSignInMethod> SignInMethods => [AppSignInMethod.Google];
    public string? UserId { get; private set; }
    public HttpClient HttpClient { get; } = new();

    public Task SignIn(IUiContext uiContext, AppSignInOptions signInOptions, CancellationToken cancellationToken)
    {
        UserId = Guid.Empty.ToString();
        return Task.CompletedTask;
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

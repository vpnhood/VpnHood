using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Device.UiContexts;

namespace VpnHood.AppLib.Test.Providers;

internal class TestAuthenticationProvider : IAppAuthenticationProvider
{
    public bool IsSignInWithGoogleSupported => false;
    public string? UserId { get; private set; }
    public HttpClient HttpClient { get; } = new ();
    public Task SignInWithGoogle(IUiContext uiContext)
    {
        UserId = Guid.Empty.ToString();
        return Task.CompletedTask;
    }

    public Task SignOut(IUiContext uiContext)
    {
        UserId = null;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }
}
using VpnHood.Client.App.Abstractions;
using VpnHood.Client.Device;
using VpnHood.Common.Utils;

namespace VpnHood.Client.App.Services;

public class AppAuthenticationService(
    VpnHoodApp vpnHoodApp, 
    IAppAuthenticationProvider accountProvider) 
    : IDisposable
{
    public bool IsSignInWithGoogleSupported => accountProvider.IsSignInWithGoogleSupported;
    public string? UserId => accountProvider.UserId;
    public HttpClient HttpClient => accountProvider.HttpClient;

    public async Task SignInWithGoogle(IUiContext uiContext)
    {
        await accountProvider.SignInWithGoogle(uiContext).VhConfigureAwait();
        await vpnHoodApp.RefreshAccount(updateCurrentClientProfile: true).VhConfigureAwait();
    }

    public async Task SignOut(IUiContext uiContext)
    {
        await accountProvider.SignOut(uiContext).VhConfigureAwait();
        await vpnHoodApp.RefreshAccount(updateCurrentClientProfile: true).VhConfigureAwait();
    }

    public void Dispose()
    {
        accountProvider.Dispose();
    }
}
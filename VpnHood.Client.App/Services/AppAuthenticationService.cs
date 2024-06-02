using VpnHood.Client.App.Abstractions;
using VpnHood.Client.Device;
using VpnHood.Common.Utils;

namespace VpnHood.Client.App.Services;

internal class AppAuthenticationService(VpnHoodApp vpnHoodApp, IAppAuthenticationService accountService)
    : IAppAuthenticationService
{
    public bool IsSignInWithGoogleSupported => accountService.IsSignInWithGoogleSupported;
    public string? UserId => accountService.UserId;
    public HttpClient HttpClient => accountService.HttpClient;

    public async Task SignInWithGoogle(IUiContext uiContext)
    {
        await accountService.SignInWithGoogle(uiContext).VhConfigureAwait();
        await vpnHoodApp.RefreshAccount(updateCurrentClientProfile:true).VhConfigureAwait();
    }

    public async Task SignOut(IUiContext uiContext)
    {
        await accountService.SignOut(uiContext).VhConfigureAwait();
        await vpnHoodApp.RefreshAccount(updateCurrentClientProfile: true).VhConfigureAwait();
    }

    public void Dispose()
    {
        accountService.Dispose();
    }
}
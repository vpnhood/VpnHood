using VpnHood.Client.App.Abstractions;
using VpnHood.Client.Device;

namespace VpnHood.Client.App.Services;

internal class AppAuthenticationService(VpnHoodApp vpnHoodApp, IAppAuthenticationService accountService)
    : IAppAuthenticationService
{
    public bool IsSignInWithGoogleSupported => accountService.IsSignInWithGoogleSupported;
    public string? UserId => accountService.UserId;
    public HttpClient HttpClient => accountService.HttpClient;

    public async Task SignInWithGoogle(IUiContext uiContext)
    {
        await accountService.SignInWithGoogle(uiContext);
        await vpnHoodApp.RefreshAccount(updateCurrentClientProfile:true);
    }

    public async Task SignOut(IUiContext uiContext)
    {
        await accountService.SignOut(uiContext);
        await vpnHoodApp.RefreshAccount(updateCurrentClientProfile: true);
    }

    public void Dispose()
    {
        accountService.Dispose();
    }
}
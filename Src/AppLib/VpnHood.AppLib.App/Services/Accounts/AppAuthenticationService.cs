using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Device.UiContexts;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Services.Accounts;

public class AppAuthenticationService(
    AppAccountService accountService,
    IAppAuthenticationProvider accountProvider)
    : IDisposable
{
    public bool IsSignInWithGoogleSupported => accountProvider.IsSignInWithGoogleSupported;
    public string? UserId => accountProvider.UserId;
    public HttpClient HttpClient => accountProvider.HttpClient;

    public async Task SignInWithGoogle(IUiContext uiContext)
    {
        await accountProvider.SignInWithGoogle(uiContext).Vhc();
        await accountService.Refresh(updateCurrentClientProfile: true).Vhc();
    }

    public async Task SignOut(IUiContext uiContext)
    {
        await accountProvider.SignOut(uiContext).Vhc();
        await accountService.Refresh(updateCurrentClientProfile: true).Vhc();
    }

    public void Dispose()
    {
        accountProvider.Dispose();
    }
}
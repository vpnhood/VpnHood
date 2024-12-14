using VpnHood.AppLibs.Abstractions;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Common.Utils;

namespace VpnHood.AppLibs.Services.Accounts;

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
        await accountProvider.SignInWithGoogle(uiContext).VhConfigureAwait();
        await accountService.Refresh(updateCurrentClientProfile: true).VhConfigureAwait();
    }

    public async Task SignOut(IUiContext uiContext)
    {
        await accountProvider.SignOut(uiContext).VhConfigureAwait();
        await accountService.Refresh(updateCurrentClientProfile: true).VhConfigureAwait();
    }

    public void Dispose()
    {
        accountProvider.Dispose();
    }
}
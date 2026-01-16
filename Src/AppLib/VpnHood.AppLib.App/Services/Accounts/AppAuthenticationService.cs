using System.Threading;
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

    public async Task SignInWithGoogle(IUiContext uiContext, CancellationToken cancellationToken)
    {
        await accountProvider.SignInWithGoogle(uiContext, cancellationToken).Vhc();
        await accountService.Refresh(updateCurrentClientProfile: true, cancellationToken).Vhc();
    }

    public async Task SignOut(IUiContext uiContext, CancellationToken cancellationToken)
    {
        await accountProvider.SignOut(uiContext, cancellationToken).Vhc();
        await accountService.Refresh(updateCurrentClientProfile: true, cancellationToken).Vhc();
    }

    public void Dispose()
    {
        accountProvider.Dispose();
    }
}
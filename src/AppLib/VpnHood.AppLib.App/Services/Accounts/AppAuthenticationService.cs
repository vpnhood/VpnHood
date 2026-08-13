using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.Core.Toolkit.Extensions;

namespace VpnHood.AppLib.Services.Accounts;

public class AppAuthenticationService(
    AppAccountService accountService,
    IAppAuthenticationProvider accountProvider)
    : IDisposable
{
    public IReadOnlyList<string> SignInMethods => accountProvider.SignInMethods;
    public string? UserId => accountProvider.UserId;
    public HttpClient HttpClient => accountProvider.HttpClient;

    public async Task SignIn(IUiContext uiContext, AppSignInOptions signInOptions, CancellationToken cancellationToken)
    {
        await accountProvider.SignIn(uiContext, signInOptions, cancellationToken).Vhc();
        await accountService.Refresh(cancellationToken).Vhc();
    }

    public async Task SignOut(IUiContext uiContext, CancellationToken cancellationToken)
    {
        await accountProvider.SignOut(uiContext, cancellationToken).Vhc();

        // The user asked to leave, so the code the account delivered leaves with them. The refresh
        // below would only DETACH it, because every other way an account disappears is not the
        // user's choice — see AppAccountService.RemoveAccountAccessCode.
        accountService.RemoveAccountAccessCode();
        await accountService.Refresh(cancellationToken).Vhc();
    }

    public void Dispose()
    {
        accountProvider.Dispose();
    }
}

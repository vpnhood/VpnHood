using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.AppLib.Api;
using VpnHood.AppLib.Services.Accounts;
using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.AppLib.Api.InProcessHost;

internal class AccountApi(VpnHoodApp app) : IAccountApi
{
    private AccountService AccountService =>
        app.Services.AccountService ??
        throw new Exception("Account service is not available at this moment.");

    public Task<Account?> Get(CancellationToken cancellationToken)
    {
        return app.Services.AccountService != null
            ? app.Services.AccountService.GetAccount(cancellationToken)
            : Task.FromResult<Account?>(null);
    }

    public Task Refresh(CancellationToken cancellationToken)
    {
        return AccountService.Refresh(cancellationToken: cancellationToken);
    }

    public Task<SignInResult> SignIn(SignInOptions signInOptions, CancellationToken cancellationToken)
    {
        if (!AccountService.AuthenticationService.ProviderIds.Contains(signInOptions.ProviderId))
            throw new NotSupportedException($"Sign-in provider is not supported. ProviderId: {signInOptions.ProviderId}");

        return AccountService.AuthenticationService.SignIn(AppUiContext.RequiredContext, signInOptions,
            cancellationToken);
    }

    public Task SignOut(CancellationToken cancellationToken)
    {
        return AccountService.AuthenticationService.SignOut(AppUiContext.RequiredContext, cancellationToken);
    }

    public Task Delete(CancellationToken cancellationToken)
    {
        return AccountService.DeleteAccount(AppUiContext.RequiredContext, cancellationToken);
    }
}
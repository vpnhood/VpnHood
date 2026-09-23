using VpnHood.AppLib.Api.Accounts;
using VpnHood.AppLib.App.Services.Accounts;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.AppLib.App.DtoConverters;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.AppLib.Api;

namespace VpnHood.AppLib.App.ApiImpl;

internal sealed class AccountApi(VpnHoodApp app) : IAccountApi
{
    private AccountService AccountService =>
        app.Services.AccountService ??
        throw new Exception("Account service is not available at this moment.");

    public async Task<Account?> Get(CancellationToken cancellationToken)
    {
        if (app.Services.AccountService is null)
            return null;

        var account = await app.Services.AccountService.GetAccount(cancellationToken).Vhc();
        return account?.ToAppDto();
    }

    public Task Refresh(CancellationToken cancellationToken)
    {
        return AccountService.Refresh(cancellationToken: cancellationToken);
    }

    public async Task<SignInResult> SignIn(SignInOptions signInOptions, CancellationToken cancellationToken)
    {
        if (!AccountService.AuthenticationService.ProviderIds.Contains(signInOptions.ProviderId))
            throw new NotSupportedException($"Sign-in provider is not supported. ProviderId: {signInOptions.ProviderId}");

        var result = await AccountService.AuthenticationService
            .SignIn(AppUiContext.RequiredContext, signInOptions.ToProvider(), cancellationToken).Vhc();
        return result.ToAppDto();
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
using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Device.UiContexts;
using VpnHood.Core.Toolkit.Exceptions;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Services.Accounts;

public class AppBillingService(AppAccountService accountService, IAppBillingProvider billingProvider) : IDisposable
{
    public BillingPurchaseState PurchaseState => billingProvider.PurchaseState;
    public string ProviderName => billingProvider.ProviderName;

    public Task<SubscriptionPlan[]> GetSubscriptionPlans(CancellationToken cancellationToken)
    {
        return billingProvider.GetSubscriptionPlans(cancellationToken);
    }

    public async Task<string> Purchase(IUiContext uiContext, PurchaseParams purchaseParams, 
        CancellationToken cancellationToken)
    {
        if (accountService.IsPremium)
            throw new AlreadyExistsException("You already have a premium subscription.");

        var ret = await billingProvider.Purchase(uiContext, purchaseParams, cancellationToken).Vhc();
        await accountService.Refresh(updateCurrentClientProfile: true, cancellationToken: cancellationToken).Vhc();
        return ret;
    }

    public void Dispose()
    {
        billingProvider.Dispose();
    }
}
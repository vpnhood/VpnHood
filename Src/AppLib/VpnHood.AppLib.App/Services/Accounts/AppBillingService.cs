using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Device.UiContexts;
using VpnHood.Core.Toolkit.ApiClients;
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

    public async Task<AppStoreInfo> GetStoreInfo(CancellationToken cancellationToken)
    {
        try {
            var subscriptionPlans = await billingProvider.GetSubscriptionPlans(cancellationToken);
            return new AppStoreInfo {
                StoreName = billingProvider.ProviderName,
                SubscriptionPlans = subscriptionPlans,
                StoreError = null
            };
        }
        catch (Exception ex) {
            return new AppStoreInfo {
                StoreName = billingProvider.ProviderName,
                SubscriptionPlans = [],
                StoreError = ex.ToApiError()
            };
        }
    }

    public async Task<string> Purchase(IUiContext uiContext, PurchaseParams purchaseParams, 
        CancellationToken cancellationToken)
    {
        if (await accountService.IsPremium(false, cancellationToken).Vhc())
            throw new AlreadyExistsException("You already have a premium subscription.");

        var ret = await billingProvider.Purchase(uiContext, purchaseParams, cancellationToken).Vhc();
        await accountService.Refresh(cancellationToken: cancellationToken).Vhc();
        return ret;
    }

    public void Dispose()
    {
        billingProvider.Dispose();
    }
}
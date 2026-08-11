using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.Core.Toolkit.ApiClients;
using VpnHood.Core.Toolkit.Exceptions;
using VpnHood.Core.Toolkit.Extensions;

namespace VpnHood.AppLib.Services.Accounts;

public class AppBillingService(
    AppAccountService accountService,
    AppBilling billing)
    : IDisposable
{
    private readonly IAppBillingProvider _billingProvider = billing.Provider;
    private readonly IAppOrderProcessor _orderProcessor = billing.OrderProcessor;
    private BillingPurchaseState _purchaseState;

    public BillingPurchaseState PurchaseState => _purchaseState != BillingPurchaseState.None
        ? _purchaseState
        : _billingProvider.PurchaseState;

    public string ProviderName => _billingProvider.ProviderName;

    /// <summary>
    /// What this app can sell, priced. Two sides answer it: the account backend says WHICH products
    /// may be sold (a store cannot list an app's own catalog, and a product the backend cannot map is
    /// a payment it cannot turn into access), the store prices and localizes them.
    /// </summary>
    public async Task<IReadOnlyList<SubscriptionPlan>> GetSubscriptionPlans(CancellationToken cancellationToken)
    {
        var productIds = await accountService.GetProductIds(cancellationToken).Vhc();
        return await _billingProvider.GetSubscriptionPlans(productIds, cancellationToken).Vhc();
    }

    public async Task<AppStoreInfo> GetStoreInfo(CancellationToken cancellationToken)
    {
        try {
            var subscriptionPlans = await GetSubscriptionPlans(cancellationToken);
            return new AppStoreInfo {
                StoreName = _billingProvider.ProviderName,
                SubscriptionPlans = subscriptionPlans,
                StoreError = null
            };
        }
        catch (Exception ex) {
            return new AppStoreInfo {
                StoreName = _billingProvider.ProviderName,
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

        try {
            _purchaseState = BillingPurchaseState.Started;
            purchaseParams.Attribution = await _orderProcessor.PreparePurchase(cancellationToken).Vhc();
            var purchaseResult = await _billingProvider.Purchase(uiContext, purchaseParams, cancellationToken).Vhc();

            _purchaseState = BillingPurchaseState.Processing;
            await _orderProcessor.CompleteOrder(purchaseResult, cancellationToken).Vhc();
            await accountService.Refresh(cancellationToken).Vhc();
            return purchaseResult.ProviderOrderId;
        }
        finally {
            _purchaseState = BillingPurchaseState.None;
        }
    }

    public async Task<string?> RestorePurchase(IUiContext uiContext, CancellationToken cancellationToken)
    {
        try {
            _purchaseState = BillingPurchaseState.Started;
            var purchaseResult = await _billingProvider.RestorePurchase(uiContext, cancellationToken).Vhc();
            if (purchaseResult == null)
                return null;

            _purchaseState = BillingPurchaseState.Processing;
            await _orderProcessor.CompleteOrder(purchaseResult, cancellationToken).Vhc();
            await accountService.Refresh(cancellationToken).Vhc();
            return purchaseResult.ProviderOrderId;
        }
        finally {
            _purchaseState = BillingPurchaseState.None;
        }
    }

    public void Dispose()
    {
        _billingProvider.Dispose();
    }
}

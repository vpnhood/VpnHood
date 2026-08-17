using VpnHood.AppLib.Abstractions.Billing;
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.Core.Toolkit.ApiClients;
using VpnHood.Core.Toolkit.Exceptions;
using VpnHood.Core.Toolkit.Extensions;

namespace VpnHood.AppLib.Services.Accounts;

public class BillingService(
    AccountService accountService,
    AppBilling billing)
    : IDisposable
{
    private readonly IBillingProvider _billingProvider = billing.Provider;
    private readonly IOrderProcessor _orderProcessor = billing.OrderProcessor;
    private PurchaseState _purchaseState;

    public PurchaseState PurchaseState => _purchaseState != PurchaseState.None
        ? _purchaseState
        : _billingProvider.PurchaseState;

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

    public async Task<StoreInfo> GetStoreInfo(CancellationToken cancellationToken)
    {
        try {
            var subscriptionPlans = await GetSubscriptionPlans(cancellationToken);
            return new StoreInfo {
                SubscriptionPlans = subscriptionPlans,
                StoreError = null
            };
        }
        catch (Exception ex) {
            return new StoreInfo {
                SubscriptionPlans = [],
                StoreError = ex.ToApiError()
            };
        }
    }

    public async Task Purchase(IUiContext uiContext, PurchaseParams purchaseParams,
        CancellationToken cancellationToken)
    {
        // Prevention, not refusal (lifecycle §8): the one moment this can be stopped is BEFORE the
        // store's payment sheet — after it the money has moved, and no store refunds on our behalf.
        // Served means served on any channel: a store subscription or the account's chosen code.
        if (await accountService.IsServed(useCache: false, cancellationToken).Vhc())
            throw new AlreadyExistsException("This account is already premium.");

        try {
            _purchaseState = PurchaseState.Started;
            var attribution = await _orderProcessor.PreparePurchase(cancellationToken).Vhc();
            var purchaseProof = await _billingProvider
                .Purchase(uiContext, purchaseParams, attribution, cancellationToken).Vhc();

            _purchaseState = PurchaseState.Processing;
            await _orderProcessor.CompleteOrder(purchaseProof, cancellationToken).Vhc();
            await accountService.Refresh(cancellationToken).Vhc();
        }
        finally {
            _purchaseState = PurchaseState.None;
        }
    }

    /// <summary>
    /// Hands the user to the store's own manage-subscriptions surface. The store does it natively;
    /// no URL reaches the UI, so no UI can open the wrong store or need a browser to exist.
    /// </summary>
    public Task OpenSubscriptionManagement(IUiContext uiContext, CancellationToken cancellationToken)
    {
        // The UI decides whether to OFFER this; here is where it is decided whether to DO it. A
        // caller that asks anyway — a stale page, a fork's own UI, a subscription billed by another
        // store — is refused with a reason instead of being handed to a store that opens nothing.
        if (!_billingProvider.IsSubscriptionManagementSupported)
            throw new NotSupportedException("This device cannot show the store's subscription management.");

        return _billingProvider.OpenSubscriptionManagement(uiContext, cancellationToken);
    }

    public async Task<bool> RestorePurchase(IUiContext uiContext, CancellationToken cancellationToken)
    {
        try {
            _purchaseState = PurchaseState.Started;
            var purchaseProof = await _billingProvider.RestorePurchase(uiContext, cancellationToken).Vhc();
            if (purchaseProof == null)
                return false;

            _purchaseState = PurchaseState.Processing;
            await _orderProcessor.CompleteOrder(purchaseProof, cancellationToken).Vhc();
            await accountService.Refresh(cancellationToken).Vhc();
            return true;
        }
        finally {
            _purchaseState = PurchaseState.None;
        }
    }

    public void Dispose()
    {
        _billingProvider.Dispose();
    }
}

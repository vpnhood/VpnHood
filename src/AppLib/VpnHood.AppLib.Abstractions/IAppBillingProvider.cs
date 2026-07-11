using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.AppLib.Abstractions;

public interface IAppBillingProvider : IDisposable
{
    string ProviderName { get; }
    Task<SubscriptionPlan[]> GetSubscriptionPlans(CancellationToken cancellationToken);

    /// <returns>Provider Order Id</returns>
    Task<string> Purchase(IUiContext uiContext, PurchaseParams purchaseParams, CancellationToken cancellationToken);

    BillingPurchaseState PurchaseState { get; }
}
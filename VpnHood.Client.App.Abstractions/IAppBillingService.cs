using VpnHood.Client.Device;

namespace VpnHood.Client.App.Abstractions;

public interface IAppBillingService : IDisposable
{
    Task<SubscriptionPlan[]> GetSubscriptionPlans();

    /// <returns>Provider Order Id</returns>
    Task<string> Purchase(IUiContext uiContext, string planId);

    BillingPurchaseState PurchaseState { get; }
}
using VpnHood.Client.Device;

namespace VpnHood.AppFramework.Abstractions;

public interface IAppBillingProvider : IDisposable
{
    Task<SubscriptionPlan[]> GetSubscriptionPlans();

    /// <returns>Provider Order Id</returns>
    Task<string> Purchase(IUiContext uiContext, string planId);

    BillingPurchaseState PurchaseState { get; } 
}
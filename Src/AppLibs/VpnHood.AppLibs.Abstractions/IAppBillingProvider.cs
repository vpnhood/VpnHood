using VpnHood.Core.Client.Device;

namespace VpnHood.AppLibs.Abstractions;

public interface IAppBillingProvider : IDisposable
{
    Task<SubscriptionPlan[]> GetSubscriptionPlans();

    /// <returns>Provider Order Id</returns>
    Task<string> Purchase(IUiContext uiContext, string planId);

    BillingPurchaseState PurchaseState { get; } 
}
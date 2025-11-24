using VpnHood.Core.Client.Device.UiContexts;

namespace VpnHood.AppLib.Abstractions;

public interface IAppBillingProvider : IDisposable
{
    string ProviderName { get; }
    Task<SubscriptionPlan[]> GetSubscriptionPlans();

    /// <returns>Provider Order Id</returns>
    Task<string> Purchase(IUiContext uiContext, string planId, string offerToken);

    BillingPurchaseState PurchaseState { get; }
}
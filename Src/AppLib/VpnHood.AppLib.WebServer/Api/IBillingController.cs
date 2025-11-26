using VpnHood.AppLib.Abstractions;

// ReSharper disable UnusedMemberInSuper.Global

namespace VpnHood.AppLib.WebServer.Api;

public interface IBillingController
{
    Task<SubscriptionPlan[]> GetSubscriptionPlans();
    Task<string> Purchase(PurchaseParams purchaseParams);
    Task<AppPurchaseOptions> GetPurchaseOptions();
}
using VpnHood.AppLib.Abstractions;

// ReSharper disable UnusedMemberInSuper.Global

namespace VpnHood.AppLib.WebServer.Api;

public interface IBillingController
{
    Task<SubscriptionPlan[]> GetSubscriptionPlans(CancellationToken cancellationToken);
    Task<string> Purchase(PurchaseParams purchaseParams, CancellationToken cancellationToken);
    Task<AppPurchaseOptions> GetPurchaseOptions(CancellationToken cancellationToken);
}
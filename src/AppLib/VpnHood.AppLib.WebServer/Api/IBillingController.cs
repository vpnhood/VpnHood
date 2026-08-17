using VpnHood.AppLib.Abstractions.Billing;

// ReSharper disable UnusedMemberInSuper.Global

namespace VpnHood.AppLib.WebServer.Api;

public interface IBillingController
{
    Task<IReadOnlyList<SubscriptionPlan>> GetSubscriptionPlans(CancellationToken cancellationToken);
    /// <summary>Run the store's payment flow and turn the purchase into access. The store's proof
    /// deliberately never reaches this layer.</summary>
    Task Purchase(PurchaseParams purchaseParams, CancellationToken cancellationToken);

    /// <summary>True when the store owned a subscription and it was restored; false when it owned none.</summary>
    Task<bool> RestorePurchase(CancellationToken cancellationToken);

    /// <summary>Open the store's manage-subscriptions surface. Offer it only while the subscription's
    /// <see cref="Abstractions.Accounts.Subscription.Management" /> is Available.</summary>
    Task OpenSubscriptionManagement(CancellationToken cancellationToken);
}

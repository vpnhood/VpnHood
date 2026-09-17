namespace VpnHood.AppLib.Abstractions.Billing;

/// <summary>
/// What the buyer chose. Everything the app attaches on the buyer's behalf travels beside it, in
/// <see cref="PurchaseAttribution" />, so this carries nothing the caller is not entitled to set.
/// </summary>
public record PurchaseParams
{
    /// <summary>
    /// Which plan to buy: <see cref="SubscriptionPlan.PlanToken" /> exactly as the store produced it —
    /// a Play offer token, an App Store product id. Opaque on the way through; only the store that
    /// issued it reads it. Not to be confused with the store's own "purchase token", which is the
    /// receipt handed back AFTER paying and travels the other way, as the answer to
    /// <see cref="IBillingProvider.Purchase" />.
    /// </summary>
    public required string PlanToken { get; set; }
}

namespace VpnHood.AppLib.Api.Billing;

/// <summary>Represents the purchase the person chose.</summary>
/// <remarks>What the app adds on the person's behalf travels apart from it, so it carries only what a caller may set.</remarks>
public record PurchaseParams
{
    /// <summary>
    /// Gets or sets the plan to buy: its opaque <see cref="SubscriptionPlan.PlanToken"/>, passed unchanged to the billing
    /// provider that made it.
    /// </summary>
    /// <remarks>
    /// Only that provider reads it. It is not the store's purchase token, the receipt a purchase gives back.
    /// </remarks>
    public required string PlanToken { get; set; }
}

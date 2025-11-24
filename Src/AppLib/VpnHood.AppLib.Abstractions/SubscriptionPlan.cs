namespace VpnHood.AppLib.Abstractions;

public class SubscriptionPlan
{
    public required string SubscriptionPlanId { get; set; }
    public required string[] PlanPrices { get; set; }
    public required string OfferToken { get; set; }
}
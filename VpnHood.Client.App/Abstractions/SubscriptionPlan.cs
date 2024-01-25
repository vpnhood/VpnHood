namespace VpnHood.Client.App.Abstractions;

public class SubscriptionPlan
{
    public required string SubscriptionPlanId { get; set; }
    public required decimal PriceAmount { get; set; }
    public required string PriceCurrency { get; set; }
}
namespace VpnHood.AppLib.Abstractions;

public class SubscriptionPlan
{
    public required string SubscriptionPlanId { get; init; }
    public required string ProductId { get; init; }
    public required double BasePrice { get; init; }
    public required double CurrentPrice { get; init; }
    public required string BaseFormattedPrice { get; init; }
    public required string CurrentFormattedPrice { get; init; }
    public required string OfferToken { get; init; }
}
namespace VpnHood.AppLib.Abstractions;

public class SubscriptionPlan
{
    public required double BasePrice { get; init; }
    public required double CurrentPrice { get; init; }
    public required string Period { get; init; }
    public required string PlanToken { get; init; }
    public required string CurrencySymbol { get; init; }
    public required string CurrencyCode { get; init; }
}
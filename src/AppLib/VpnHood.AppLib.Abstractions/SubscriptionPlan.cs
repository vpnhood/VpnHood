namespace VpnHood.AppLib.Abstractions;

public class SubscriptionPlan
{
    /// <summary>Recurring price of the plan's billing period.</summary>
    public required double BasePrice { get; init; }

    /// <summary>First price the user actually pays: the introductory price if the offer has one, otherwise the base price.</summary>
    public required double CurrentPrice { get; init; }

    /// <summary>ISO 8601 duration of the recurring billing period, e.g. "P1M".</summary>
    public required string Period { get; init; }

    /// <summary>ISO 8601 duration of the free-trial phase, if the offer has one.</summary>
    public string? TrialPeriod { get; init; }

    public required string PlanToken { get; init; }
    public required string CurrencySymbol { get; init; }
    public required string CurrencyCode { get; init; }
}

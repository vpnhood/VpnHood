namespace VpnHood.AppLib.Abstractions.Billing;

/// <summary>Represents a subscription's plan as the store prices it, in the currency the store quotes.</summary>
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

    /// <summary>Gets the store's token of the plan, which the purchase names.</summary>
    public required string PlanToken { get; init; }

    /// <summary>Gets the currency's symbol, as <c>$</c>.</summary>
    public required string CurrencySymbol { get; init; }

    /// <summary>Gets the currency's ISO 4217 code, as <c>USD</c>.</summary>
    public required string CurrencyCode { get; init; }

    /// <summary>The page the plan is bought on, for a store that sells on the web: the UI opens it as it opens any link. Null where the purchase happens in the app.</summary>
    public Uri? CheckoutUrl { get; init; }
}

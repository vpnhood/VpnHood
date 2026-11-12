namespace VpnHood.AppLib.Api.Billing;

/// <summary>Represents a subscription's plan as the store prices it, in the currency the store quotes.</summary>
public class SubscriptionPlan
{
    /// <summary>Gets the price of each billing period.</summary>
    public required double BasePrice { get; init; }

    /// <summary>Gets the first price the person pays: the introductory price where the offer has one, else <see cref="BasePrice"/>.</summary>
    public required double CurrentPrice { get; init; }

    /// <summary>Gets the billing period, as an ISO 8601 duration, as <c>P1M</c>.</summary>
    public required string Period { get; init; }

    /// <summary>Gets the free trial's period, as an ISO 8601 duration, or <see langword="null"/> where the offer has none.</summary>
    public string? TrialPeriod { get; init; }

    /// <summary>Gets the plan's opaque token, which a purchase names; only the billing provider that made it reads it.</summary>
    public required string PlanToken { get; init; }

    /// <summary>Gets the currency's symbol, as <c>$</c>.</summary>
    public required string CurrencySymbol { get; init; }

    /// <summary>Gets the currency's ISO 4217 code, as <c>USD</c>.</summary>
    public required string CurrencyCode { get; init; }

    /// <summary>
    /// Gets the page the plan is bought on, for a store that sells on the web, which the UI opens as any link; or
    /// <see langword="null"/> where the purchase happens in the app.
    /// </summary>
    public Uri? CheckoutUrl { get; init; }
}

namespace VpnHood.AppLib.Portal.Dto;

/// <summary>
/// One sellable plan of a web-distributed app, priced and ready to buy. The portal serves these
/// only to web builds — a store build prices its plans at its store — and every field is display
/// or navigation data: the app never assembles a purchase link or computes a price itself.
/// </summary>
public class PortalPlan
{
    public required string PlanId { get; init; }

    /// <summary>ISO-8601 duration of one billing cycle, e.g. "P1M", "P1Y".</summary>
    public required string BillingPeriod { get; init; }

    /// <summary>Decimal price per cycle, exactly as the checkout will bill it.</summary>
    public required string PriceAmount { get; init; }

    /// <summary>ISO 4217 code of <see cref="PriceAmount"/>.</summary>
    public required string PriceCurrency { get; init; }

    /// <summary>
    /// The display symbol the portal's own checkout renders before the amount — show the price
    /// exactly as "{symbol}{amount}". A symbol-less currency arrives as its code plus a space.
    /// </summary>
    public required string PriceCurrencySymbol { get; init; }

    /// <summary>Checkout URL with plan and currency preselected; open it in the system browser.</summary>
    public required Uri PurchaseUrl { get; init; }
}

namespace VpnHood.AppLib.Abstractions.Accounts;

/// <summary>
/// The store subscription serving an account. Its very presence is the answer to "is this account
/// subscribed" — the facts below are only meaningful together, so they live or go as one object
/// rather than as eight independently-nullable fields on the account.
/// </summary>
public class Subscription
{
    /// <summary>
    /// The store that billed it ("googleplay", "appstore", "microsoft"). This is the BILLING store,
    /// which legitimately differs from the store this build ships to (bought on Android, now signed in
    /// on an iPhone), so it never answers "which app is this". An id, as the name says: the app
    /// decides on it, the UI never shows it.
    /// </summary>
    public required string StoreId { get; set; }

    /// <summary>When the subscription began.</summary>
    public DateTime? CreatedTime { get; set; }

    public DateTime? ExpirationTime { get; set; }

    /// <summary>What the STORE charged for the current period — not a catalogue price.</summary>
    public decimal? PriceAmount { get; set; }

    public string? PriceCurrency { get; set; }

    /// <summary>
    /// The period <see cref="PriceAmount" /> buys, as an ISO-8601 duration ("P1M", "P1Y", …) — the same
    /// vocabulary the store uses for plan periods. Without it the UI cannot say what the price is
    /// *per*, and a yearly subscription reads as a monthly one.
    /// </summary>
    public string? BillingPeriod { get; set; }

    public bool? IsAutoRenew { get; set; }

    /// <summary>
    /// Where this subscription can be managed from here, and when it cannot, why. A URL never
    /// crosses to the UI: the store provider performs it, so no UI holds a store address. Composed
    /// by the account provider, which is the only place that knows both who billed the subscription
    /// and what this device can show — the portal never sends it.
    /// </summary>
    public SubscriptionManagement Management { get; set; }
}

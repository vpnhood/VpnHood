namespace VpnHood.AppLib.Api.Accounts;

/// <summary>Represents the store subscription that serves an account; it is there only where the account has one.</summary>
public class Subscription
{
    /// <summary>
    /// Gets or sets the ID of the store that billed the subscription, as <c>googleplay</c>, <c>appstore</c> or
    /// <c>microsoft</c>.
    /// </summary>
    /// <remarks>
    /// It can differ from the store this build comes from, as for a subscription bought on Android with an iPhone now
    /// signed in: the app decides by it, and the UI never shows it.
    /// </remarks>
    public required string StoreId { get; set; }

    /// <summary>Gets or sets when the account backend first recorded the subscription, or <see langword="null"/>.</summary>
    public DateTime? CreatedTime { get; set; }

    /// <summary>Gets or sets when the current period ends, or <see langword="null"/>.</summary>
    public DateTime? ExpirationTime { get; set; }

    /// <summary>Gets or sets the last charge the store reported for the subscription, not a list price, or <see langword="null"/>.</summary>
    public decimal? PriceAmount { get; set; }

    /// <summary>Gets or sets the currency of <see cref="PriceAmount"/>, or <see langword="null"/>.</summary>
    public string? PriceCurrency { get; set; }

    /// <summary>
    /// Gets or sets the period <see cref="PriceAmount"/> pays for, as an ISO 8601 duration (<c>P1M</c>, <c>P1Y</c>), or
    /// <see langword="null"/>.
    /// </summary>
    public string? BillingPeriod { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the subscription renews at the period's end, or
    /// <see langword="null"/> where that is not known.
    /// </summary>
    public bool? IsAutoRenew { get; set; }

    /// <summary>
    /// Gets or sets where the subscription can be managed from this device, or why not. The default is
    /// <see cref="SubscriptionManagement.AnotherStore"/>.
    /// </summary>
    /// <remarks>
    /// The account provider sets it, as it knows both the store that billed it and what this device can show. The UI gets
    /// no store address: the app opens the store's screen itself.
    /// </remarks>
    public SubscriptionManagement Management { get; set; }
}

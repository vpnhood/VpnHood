namespace VpnHood.AppLib.Abstractions;

public class AppAccount
{
    public required string UserId { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? SubscriptionId { get; set; }
    public string? ProviderPlanId { get; set; }
    public DateTime? CreatedTime { get; set; }
    public DateTime? ExpirationTime { get; set; }
    public decimal? PriceAmount { get; set; }
    public string? PriceCurrency { get; set; }

    // The period PriceAmount buys, as an ISO-8601 duration ("P1M", "P1Y", …) — the same
    // vocabulary the store uses for plan periods. Without it the UI cannot say what the
    // price is *per*, and a yearly subscription reads as a monthly one.
    public string? PriceBillingPeriod { get; set; }
    public bool? IsAutoRenew { get; set; }
    public string? ProviderSubscriptionId { get; set; }

    // The manage-subscriptions page for the active subscription — only when THIS build's store billed
    // it (a subscription billed by another platform's store has no page this device can open). The UI
    // renders it verbatim when present and shows a neutral "managed where purchased" hint otherwise.
    public Uri? SubscriptionManagementUrl { get; set; }
}
namespace VpnHood.AppLib.Portal.Dto;

/// <summary>POST /billing/purchases response, and the items of GET /account/entitlements.</summary>
public class PortalEntitlement
{
    public const string StateProvisioned = "provisioned";
    public const string StatePending = "pending";

    public required string State { get; init; }
    public string? AccessCode { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public string? PlanId { get; init; }

    /// <summary>The store that billed this entitlement (PortalStoreIds) — may differ from the build's own store.</summary>
    public string? Store { get; init; }

    /// <summary>When the subscription began. Null while the entitlement is pending.</summary>
    public DateTime? PurchasedAt { get; init; }

    /// <summary>Whether the store will bill the next period. False once the buyer cancels, until <see cref="ExpiresAt"/>.</summary>
    public bool? AutoRenewing { get; init; }

    /// <summary>What the STORE charged for the current period — not a catalogue price, so it reflects the store's own rounding.</summary>
    public decimal? PriceAmount { get; init; }

    /// <summary>ISO 4217 currency of <see cref="PriceAmount"/>.</summary>
    public string? PriceCurrency { get; init; }

    /// <summary>The recurrence as an ISO-8601 duration ("P1M", "P1Y", …); null for a one-off entitlement.</summary>
    public string? BillingPeriod { get; init; }
}

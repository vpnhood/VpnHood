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
}

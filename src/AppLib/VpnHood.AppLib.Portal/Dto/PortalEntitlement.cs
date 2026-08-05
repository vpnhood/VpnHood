namespace VpnHood.AppLib.Portal.Dto;

/// <summary>purchase.verify response and entitlement.get list items.</summary>
public class PortalEntitlement
{
    public const string StateProvisioned = "provisioned";
    public const string StatePending = "pending";
    public const string StateAwaitingEmailVerification = "awaiting_email_verification";

    public required string State { get; init; }
    public string? AccessCode { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public string? PlanId { get; init; }
}

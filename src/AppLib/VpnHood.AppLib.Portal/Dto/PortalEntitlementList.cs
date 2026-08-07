namespace VpnHood.AppLib.Portal.Dto;

/// <summary>GET /account/entitlements — the account's live entitlements, newest first.</summary>
public class PortalEntitlementList
{
    public required IReadOnlyList<PortalEntitlement> Items { get; init; }
}

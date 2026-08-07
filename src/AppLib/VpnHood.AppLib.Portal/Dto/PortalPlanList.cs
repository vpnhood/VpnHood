namespace VpnHood.AppLib.Portal.Dto;

/// <summary>GET /billing/plans response.</summary>
public class PortalPlanList
{
    public required IReadOnlyList<PortalPlan> Items { get; init; }
}

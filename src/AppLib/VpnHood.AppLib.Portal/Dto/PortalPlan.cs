namespace VpnHood.AppLib.Portal.Dto;

/// <summary>An item of GET /billing/plans.</summary>
public class PortalPlan
{
    /// <summary>The portal's identifier: storeProductId, or storeProductId/basePlanId.</summary>
    public required string PlanId { get; init; }

    public required string StoreProductId { get; init; }

    /// <summary>The base plan / billing period within the store product; empty when the product has none.</summary>
    public required string BasePlanId { get; init; }
}

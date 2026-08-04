namespace VpnHood.AppLib.Abstractions;

public record PurchaseParams
{
    public required string PurchaseToken { get; set; }

    /// <summary>Set by the billing orchestration from the order processor; client-supplied values are overwritten.</summary>
    public AppPurchaseAttribution? Attribution { get; set; }
}

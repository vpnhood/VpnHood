namespace VpnHood.AppLib.Abstractions;

public record PurchaseParams
{
    public required string PurchaseToken { get; set; }
}
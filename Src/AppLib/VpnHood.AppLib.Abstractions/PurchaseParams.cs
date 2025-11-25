namespace VpnHood.AppLib.Abstractions;

public record PurchaseParams
{
    public required string ProductId { get; set; }
    public required string OfferToken { get; set; }
}
namespace VpnHood.AppLib.Abstractions;

public class AppPurchaseResult
{
    public required string ProviderOrderId { get; init; }

    /// <summary>
    /// Store-specific proof of purchase for backend verification when the backend cannot rely on
    /// store webhooks alone. GooglePlay: purchase token. AppStore: JWS transaction. MicrosoftStore: store id key.
    /// </summary>
    public string? PurchaseData { get; init; }
}

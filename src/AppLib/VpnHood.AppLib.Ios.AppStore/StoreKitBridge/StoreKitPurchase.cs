namespace VpnHood.AppLib.Ios.AppStore.StoreKitBridge;

/// <summary>The outcome of a StoreKit 2 purchase or entitlement lookup.</summary>
public class StoreKitPurchase
{
    public const string StatePurchased = "purchased";
    public const string StatePending = "pending";
    public const string StateCancelled = "cancelled";

    public required string State { get; init; }
    public string? TransactionId { get; init; }
    public string? OriginalTransactionId { get; init; }

    /// <summary>The SK2 signed transaction (JWS) — the proof POST /billing/purchases sends to the portal.</summary>
    public string? Jws { get; init; }
}

namespace VpnHood.AppLib.Ios.AppStore.StoreKitBridge;

/// <summary>
/// The StoreKit 2 surface this package needs, as a seam: the native
/// implementation crosses into the VpnHoodStoreKit Swift facade; tests can
/// substitute a fake without any Swift.
/// </summary>
public interface IStoreKitBridge
{
    Task<IReadOnlyList<StoreKitProduct>> LoadProducts(IReadOnlyList<string> productIds,
        CancellationToken cancellationToken);

    /// <summary>Run the purchase flow. appAccountToken becomes Apple's per-purchase account binding.</summary>
    Task<StoreKitPurchase> Purchase(string productId, Guid appAccountToken, CancellationToken cancellationToken);

    /// <summary>The latest current entitlement, or null when the user owns nothing.</summary>
    Task<StoreKitPurchase?> CurrentEntitlement(CancellationToken cancellationToken);
}

public class StoreKitProduct
{
    public required string Id { get; init; }

    /// <summary>Decimal price in the store currency (the recurring, non-introductory price).</summary>
    public required double Price { get; init; }

    /// <summary>First price actually paid: the introductory offer if one applies, else Price.</summary>
    public required double CurrentPrice { get; init; }

    /// <summary>ISO 8601 duration of the billing period, e.g. "P1M".</summary>
    public required string PeriodIso { get; init; }

    /// <summary>ISO 8601 duration of the free-trial phase, when the eligible offer has one.</summary>
    public string? TrialPeriodIso { get; init; }

    public required string CurrencyCode { get; init; }
    public required string CurrencySymbol { get; init; }
}

public class StoreKitPurchase
{
    public const string StatePurchased = "purchased";
    public const string StatePending = "pending";
    public const string StateCancelled = "cancelled";

    public required string State { get; init; }
    public string? TransactionId { get; init; }
    public string? OriginalTransactionId { get; init; }

    /// <summary>The SK2 signed transaction (JWS) — the proof purchase.verify posts to the portal.</summary>
    public string? Jws { get; init; }
}

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

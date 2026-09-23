namespace VpnHood.AppLib.Abstractions.Billing;

/// <summary>
/// Backend-specific purchase reconciliation. The billing provider talks to the platform store;
/// the order processor talks to the account backend before and after the store purchase.
/// </summary>
public interface IOrderProcessor
{
    /// <summary>
    /// Called before the store's payment flow opens: answers what the purchase must be attributed
    /// to, and is the point where a backend could also reserve an order of its own.
    /// </summary>
    Task<PurchaseAttribution> PreparePurchase(CancellationToken cancellationToken);

    /// <summary>
    /// Hand the store's proof of purchase to the backend and wait until it is turned into access.
    /// </summary>
    Task CompleteOrder(PurchaseProof purchaseProof, CancellationToken cancellationToken);
}

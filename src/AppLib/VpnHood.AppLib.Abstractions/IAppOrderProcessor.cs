namespace VpnHood.AppLib.Abstractions;

/// <summary>
/// Backend-specific purchase reconciliation. The billing provider talks to the platform store;
/// the order processor talks to the account backend before and after the store purchase.
/// </summary>
public interface IAppOrderProcessor
{
    /// <summary>Build the attribution the billing provider must attach to the store purchase.</summary>
    Task<AppPurchaseAttribution> PreparePurchase(CancellationToken cancellationToken);

    /// <summary>Report the store purchase to the backend and wait until the order is processed.</summary>
    Task CompleteOrder(AppPurchaseResult purchaseResult, CancellationToken cancellationToken);
}

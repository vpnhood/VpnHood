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
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is what the purchase must be attributed to.</returns>
    /// <exception cref="System.Security.Authentication.AuthenticationException">Nobody is signed in.</exception>
    Task<PurchaseAttribution> PreparePurchase(CancellationToken cancellationToken);

    /// <summary>
    /// Hand the store's proof of purchase to the backend and wait until it is turned into access.
    /// </summary>
    /// <param name="purchaseProof">The store's proof of purchase.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the purchase is turned into access.</returns>
    /// <exception cref="InvalidOperationException">
    /// The store had not settled the payment by the processor's last try; a later call may succeed.
    /// </exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">The account backend refused the request.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task CompleteOrder(PurchaseProof purchaseProof, CancellationToken cancellationToken);
}

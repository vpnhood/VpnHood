using VpnHood.AppLib.Abstractions.Billing;

namespace VpnHood.AppLib.Test.Providers;

internal class TestOrderProcessor : IOrderProcessor
{
    public PurchaseAttribution Attribution { get; set; } = new() { UserId = Guid.Empty.ToString() };

    public List<PurchaseProof> CompletedOrders { get; } = [];

    /// <summary>
    /// What the backend does once it has verified the order — this is where a store payment
    /// becomes an entitlement. Tests use it to grant the subscription the purchase just paid for.
    /// </summary>
    public Func<PurchaseProof, Task>? OnCompleteOrder { get; set; }

    public Task<PurchaseAttribution> PreparePurchase(CancellationToken cancellationToken)
    {
        return Task.FromResult(Attribution);
    }

    public async Task CompleteOrder(PurchaseProof purchaseProof, CancellationToken cancellationToken)
    {
        CompletedOrders.Add(purchaseProof);
        if (OnCompleteOrder != null)
            await OnCompleteOrder(purchaseProof);
    }
}

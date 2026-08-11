using VpnHood.AppLib.Abstractions;

namespace VpnHood.AppLib.Test.Providers;

internal class TestOrderProcessor : IAppOrderProcessor
{
    public AppPurchaseAttribution Attribution { get; set; } = new() {
        AccountId = Guid.Empty.ToString()
    };

    public List<AppPurchaseResult> CompletedOrders { get; } = [];

    /// <summary>
    /// What the backend does once it has verified the order — this is where a store payment
    /// becomes an entitlement. Tests use it to grant the subscription the purchase just paid for.
    /// </summary>
    public Func<AppPurchaseResult, Task>? OnCompleteOrder { get; set; }

    public Task<AppPurchaseAttribution> PreparePurchase(CancellationToken cancellationToken)
    {
        return Task.FromResult(Attribution);
    }

    public async Task CompleteOrder(AppPurchaseResult purchaseResult, CancellationToken cancellationToken)
    {
        CompletedOrders.Add(purchaseResult);
        if (OnCompleteOrder != null)
            await OnCompleteOrder(purchaseResult);
    }
}

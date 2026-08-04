using VpnHood.AppLib.Abstractions;

namespace VpnHood.AppLib.Test.Providers;

internal class TestOrderProcessor : IAppOrderProcessor
{
    public AppPurchaseAttribution Attribution { get; set; } = new() {
        AccountId = Guid.Empty.ToString()
    };

    public List<AppPurchaseResult> CompletedOrders { get; } = [];

    public Task<AppPurchaseAttribution> PreparePurchase(CancellationToken cancellationToken)
    {
        return Task.FromResult(Attribution);
    }

    public Task CompleteOrder(AppPurchaseResult purchaseResult, CancellationToken cancellationToken)
    {
        CompletedOrders.Add(purchaseResult);
        return Task.CompletedTask;
    }
}

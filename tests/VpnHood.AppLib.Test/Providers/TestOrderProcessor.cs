using VpnHood.AppLib.Abstractions;

namespace VpnHood.AppLib.Test.Providers;

internal class TestOrderProcessor : IAppOrderProcessor
{
    public Task<AppPurchaseAttribution> PreparePurchase(CancellationToken cancellationToken)
    {
        return Task.FromResult(new AppPurchaseAttribution {
            AccountId = Guid.Empty.ToString()
        });
    }

    public Task CompleteOrder(AppPurchaseResult purchaseResult, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

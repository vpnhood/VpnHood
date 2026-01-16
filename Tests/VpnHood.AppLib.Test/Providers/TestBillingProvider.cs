using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Device.UiContexts;

namespace VpnHood.AppLib.Test.Providers;

internal class TestBillingProvider : IAppBillingProvider
{
    public Exception? PurchaseException { get; set; }
    public Exception? SubscriptionPlanException { get; set; }

    public string ProviderName => "Test";

    public async Task<SubscriptionPlan[]> GetSubscriptionPlans(CancellationToken cancellationToken)
    {
        if (SubscriptionPlanException != null)
            throw SubscriptionPlanException;

        await Task.CompletedTask;
        return [
            new SubscriptionPlan {
                BasePrice = 1000,
                CurrentPrice = 9000,
                Period = "1M",
                PlanToken = "test_plan_1m",
                CurrencySymbol = "$",
                CurrencyCode = "USD"
            }
        ];
    }

    public async Task<string> Purchase(IUiContext uiContext, PurchaseParams purchaseParams, CancellationToken cancellationToken)
    {
        if (PurchaseException != null)
            throw PurchaseException;

        await Task.CompletedTask;
        return Guid.NewGuid().ToString();
    }

    public BillingPurchaseState PurchaseState => BillingPurchaseState.None;

    public void Dispose()
    {
    }
}
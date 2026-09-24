using VpnHood.AppLib.Api.Billing;

namespace VpnHood.AppLib.Api.HttpClients;

// IBillingApi over HTTP: the routes of BillingController, one for one.
internal sealed class BillingClient(HttpClient httpClient) : AppApiClientBase(httpClient), IBillingApi
{
    private const string BaseUrl = "api/billing/";

    public Task<IReadOnlyList<SubscriptionPlan>> GetSubscriptionPlans(CancellationToken cancellationToken)
    {
        return HttpGetAsync<IReadOnlyList<SubscriptionPlan>>(BaseUrl + "subscription-plans", null, cancellationToken);
    }

    public Task Purchase(PurchaseParams purchaseParams, CancellationToken cancellationToken)
    {
        return HttpPostAsync(BaseUrl + "purchase", null, purchaseParams, cancellationToken);
    }

    public Task<bool> RestorePurchase(CancellationToken cancellationToken)
    {
        return HttpPostAsync<bool>(BaseUrl + "restore-purchase", null, null, cancellationToken);
    }

    public Task OpenSubscriptionManagement(CancellationToken cancellationToken)
    {
        return HttpPostAsync(BaseUrl + "subscription-management", null, null, cancellationToken);
    }
}

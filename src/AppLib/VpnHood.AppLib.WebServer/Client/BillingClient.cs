using VpnHood.AppLib.Abstractions.Billing;
using VpnHood.AppLib.WebServer.Api;

namespace VpnHood.AppLib.WebServer.Client;

// IBillingController over HTTP: the routes of BillingController, one for one.
internal sealed class BillingClient(HttpClient httpClient) : AppApiClientBase(httpClient), IBillingController
{
    private const string BaseUrl = "api/billing/";

    public Task<IReadOnlyList<SubscriptionPlan>> GetSubscriptionPlans(CancellationToken cancellationToken)
    {
        return GetAsync<IReadOnlyList<SubscriptionPlan>>(BaseUrl + "subscription-plans", null, cancellationToken);
    }

    public Task Purchase(PurchaseParams purchaseParams, CancellationToken cancellationToken)
    {
        return PostBodyAsync(BaseUrl + "purchase", purchaseParams, cancellationToken);
    }

    public Task<bool> RestorePurchase(CancellationToken cancellationToken)
    {
        return PostAsync<bool>(BaseUrl + "restore-purchase", null, cancellationToken);
    }

    public Task OpenSubscriptionManagement(CancellationToken cancellationToken)
    {
        return PostAsync(BaseUrl + "subscription-management", null, cancellationToken);
    }
}

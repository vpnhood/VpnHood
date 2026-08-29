using System.Globalization;
using VpnHood.AppLib.Abstractions.Billing;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.Core.Toolkit.Extensions;

namespace VpnHood.AppLib.Portal;

/// <summary>
/// The web-distribution channel's billing provider: the portal IS the store. Plans and prices come
/// from GET /billing/plans — the same rows the checkout bills, in one currency that every checkout
/// URL pins, so a shown price can never disagree with the invoice. Purchase opens the checkout in
/// the system browser (how is platform business — the app passes its own opener) and ends the
/// in-app flow as a <see cref="UserCanceledException" />, which the UI already renders as silence:
/// an external checkout has no in-app completion to report, and delivery arrives through the
/// signed-in account like every web purchase.
/// </summary>
public class PortalWebBillingProvider : IBillingProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _packageName;
    private readonly Func<IUiContext, Uri, CancellationToken, Task> _openUrl;
    private IReadOnlyDictionary<string, Uri> _checkoutUrls = new Dictionary<string, Uri>();

    /// <param name="openUrl">
    /// Opens a URL in the SYSTEM browser. Platform business, so the app that knows its platform
    /// passes it in — an Intent on Android, the shell on desktop.
    /// </param>
    public PortalWebBillingProvider(Uri portalBaseUrl, string packageName,
        Func<IUiContext, Uri, CancellationToken, Task> openUrl, bool ignoreSslVerification = false)
    {
        _packageName = packageName;
        _openUrl = openUrl;

        // this provider owns its transport, like PortalAccountProvider and for the same reason
        var handler = new HttpClientHandler();
        if (ignoreSslVerification) handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        _httpClient = new HttpClient(handler) { BaseAddress = portalBaseUrl };
    }

    public string ProviderId => StoreIds.Web;

    // Web subscriptions are managed on the website account page, which the portal owns — there is
    // no store surface on the device to show, so the UI is told to say "manage it where you bought
    // it" rather than being handed a control that opens nothing.
    public bool IsSubscriptionManagementSupported => false;

    public PurchaseState PurchaseState => PurchaseState.None;

    public Task OpenSubscriptionManagement(IUiContext uiContext, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("A web subscription is managed on the website, not on this device.");
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetSubscriptionPlans(IReadOnlyList<string> productIds,
        CancellationToken cancellationToken)
    {
        var apiClient = new PortalApiClient(_httpClient);
        var plans = await apiClient.ListPlans(StoreIds.Web, _packageName, cancellationToken).Vhc();

        // like every store: price exactly what the backend says is sellable, nothing more
        plans = plans.Where(plan => productIds.Contains(plan.PlanId)).ToArray();
        _checkoutUrls = plans.ToDictionary(plan => plan.PlanId, plan => plan.PurchaseUrl);

        return plans.Select(plan => new SubscriptionPlan {
            PlanToken = plan.PlanId,
            Period = plan.BillingPeriod,
            BasePrice = double.Parse(plan.PriceAmount, CultureInfo.InvariantCulture),
            CurrentPrice = double.Parse(plan.PriceAmount, CultureInfo.InvariantCulture),
            CurrencyCode = plan.PriceCurrency,
            // the portal's own symbol: the checkout renders "{symbol}{amount}", so the card matches it
            CurrencySymbol = plan.PriceCurrencySymbol
        }).ToArray();
    }

    public async Task<PurchaseProof> Purchase(IUiContext uiContext, PurchaseParams purchaseParams,
        PurchaseAttribution attribution, CancellationToken cancellationToken)
    {
        // the chosen plan's own checkout URL, fetched fresh if this instance has not priced the
        // plans yet — a purchase always follows a rendered plans page, but never trust that ordering
        if (!_checkoutUrls.TryGetValue(purchaseParams.PlanToken, out var checkoutUrl)) {
            await GetSubscriptionPlans([purchaseParams.PlanToken], cancellationToken).Vhc();
            if (!_checkoutUrls.TryGetValue(purchaseParams.PlanToken, out checkoutUrl))
                throw new InvalidOperationException($"The portal sells no such plan: {purchaseParams.PlanToken}");
        }

        await _openUrl(uiContext, checkoutUrl, cancellationToken).Vhc();

        // Not an error and not a completion: the checkout continues in the browser, where this app
        // cannot see it end. The UI shows this exception as silence, and the purchase reaches the
        // account server-side — the next account refresh delivers it.
        throw new UserCanceledException("The checkout continues in the system browser.");
    }

    public Task<PurchaseProof?> RestorePurchase(IUiContext uiContext, CancellationToken cancellationToken)
    {
        // a web purchase lives in the account, not on the device — signing in restores it by itself
        return Task.FromResult<PurchaseProof?>(null);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

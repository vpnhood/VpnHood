using System.Globalization;
using VpnHood.AppLib.Abstractions.Billing;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppLib.Portal;

/// <summary>
/// The web-distribution channel's billing provider: the portal IS the store. Plans and prices come
/// from GET /billing/plans — the same rows the checkout bills, in one currency that every checkout
/// URL pins, so a shown price can never disagree with the invoice. Each plan carries its checkout
/// page (<see cref="SubscriptionPlan.CheckoutUrl" />), which the UI opens as it opens any link — on
/// a TV, as a QR code for the phone — so nothing is bought in the app: delivery arrives through the
/// signed-in account like every web purchase.
/// </summary>
public class PortalWebBillingProvider : IBillingProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _packageName;

    /// <param name="portalBaseUrl">The portal the plans are listed from and the checkout opens on.</param>
    /// <param name="packageName">The app the plans are listed for, as the portal names it.</param>
    /// <param name="ignoreSslVerification">Accepts any server certificate: a development portal's.</param>
    public PortalWebBillingProvider(Uri portalBaseUrl, string packageName, bool ignoreSslVerification = false)
    {
        _packageName = packageName;

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
        plans = [.. plans.Where(plan => productIds.Contains(plan.PlanId))];

        return [.. plans.Select(plan => new SubscriptionPlan {
            PlanToken = plan.PlanId,
            Period = plan.BillingPeriod,
            BasePrice = double.Parse(plan.PriceAmount, CultureInfo.InvariantCulture),
            CurrentPrice = double.Parse(plan.PriceAmount, CultureInfo.InvariantCulture),
            CurrencyCode = plan.PriceCurrency,
            // the portal's own symbol: the checkout renders "{symbol}{amount}", so the card matches it
            CurrencySymbol = plan.PriceCurrencySymbol,
            CheckoutUrl = plan.PurchaseUrl
        })];
    }

    // The UI opens the plan's checkout page instead; the purchase reaches the account server-side,
    // and the next account refresh delivers it.
    public Task<PurchaseProof> Purchase(IUiContext uiContext, PurchaseParams purchaseParams,
        PurchaseAttribution attribution, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("A web plan is bought on its checkout page (SubscriptionPlan.CheckoutUrl).");
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

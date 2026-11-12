using System.Globalization;
using VpnHood.AppLib.Abstractions.Billing;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppLib.Portal;

/// <summary>
/// The web-distribution channel's billing provider: the portal IS the store. Plans and prices come
/// from GET /billing/plans — the rows the checkout bills, priced in the portal's default currency,
/// which each checkout URL names. Each plan carries its checkout
/// page (<see cref="SubscriptionPlan.CheckoutUrl" />), which the UI opens as it opens any link — on
/// a TV, as a QR code for the phone — so nothing is bought in the app: delivery arrives through the
/// signed-in account like every web purchase.
/// </summary>
public class PortalWebBillingProvider : IBillingProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _packageName;

    /// <summary>Initializes a new instance of the <see cref="PortalWebBillingProvider"/> class.</summary>
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

    /// <inheritdoc/>
    /// <remarks>It is <see cref="StoreIds.Web"/>.</remarks>
    public string ProviderId => StoreIds.Web;

    // Web subscriptions are managed on the website account page, which the portal owns — there is
    // no store surface on the device to show, so the UI is told to say "manage it where you bought
    // it" rather than being handed a control that opens nothing.
    /// <inheritdoc/>
    /// <remarks>
    /// It is <see langword="false"/>: a web subscription is managed on the website's account page, which no store on the
    /// device shows.
    /// </remarks>
    public bool IsSubscriptionManagementSupported => false;

    /// <inheritdoc/>
    /// <remarks>It is always <see cref="PurchaseState.None"/>: nothing is bought in the app.</remarks>
    public PurchaseState PurchaseState => PurchaseState.None;

    /// <inheritdoc/>
    /// <remarks>It always throws <see cref="NotSupportedException"/>.</remarks>
    public Task OpenSubscriptionManagement(IUiContext? uiContext, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("A web subscription is managed on the website, not on this device.");
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The portal lists the app's web plans for a request without a session, so in its default currency, and each
    /// plan's checkout page (<see cref="SubscriptionPlan.CheckoutUrl"/>) names that currency; only the given products
    /// are kept.
    /// </remarks>
    public async Task<IReadOnlyList<SubscriptionPlan>> GetSubscriptionPlans(IReadOnlyList<string> productIds,
        CancellationToken cancellationToken)
    {
        var apiClient = new PortalApiClient(_httpClient);
        var plans = await apiClient.ListPlans(StoreIds.Web, _packageName, cancellationToken).Vhc();

        // like every store: price exactly what the backend says is sellable, nothing more
        plans = plans.Where(plan => productIds.Contains(plan.PlanId)).ToArray();

        return plans.Select(plan => new SubscriptionPlan {
            PlanToken = plan.PlanId,
            Period = plan.BillingPeriod,
            BasePrice = double.Parse(plan.PriceAmount, CultureInfo.InvariantCulture),
            CurrentPrice = double.Parse(plan.PriceAmount, CultureInfo.InvariantCulture),
            CurrencyCode = plan.PriceCurrency,
            // the portal's own symbol: the checkout renders "{symbol}{amount}", so the card matches it
            CurrencySymbol = plan.PriceCurrencySymbol,
            CheckoutUrl = plan.PurchaseUrl
        }).ToArray();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It always throws <see cref="NotSupportedException"/>: a web plan is bought on its checkout page, and the purchase
    /// reaches the account on the server once it is provisioned and the account is linked to the website's client,
    /// which a sign-in does; an account refresh then reads the account's code and subscription, as the portal ranks
    /// them. An account first signed in with an identity provider may need another sign-in for that link.
    /// </remarks>
    public Task<PurchaseProof> Purchase(IUiContext? uiContext, PurchaseParams purchaseParams,
        PurchaseAttribution attribution, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("A web plan is bought on its checkout page (SubscriptionPlan.CheckoutUrl).");
    }

    /// <inheritdoc/>
    /// <remarks>It gives <see langword="null"/>: a web purchase lives in the account, and signing in restores it.</remarks>
    public Task<PurchaseProof?> RestorePurchase(IUiContext? uiContext, CancellationToken cancellationToken)
    {
        // a web purchase lives in the account, not on the device — signing in restores it by itself
        return Task.FromResult<PurchaseProof?>(null);
    }

    /// <inheritdoc/>
    /// <remarks>It does nothing: this provider gives no proof to finish.</remarks>
    public Task FinishPurchase(PurchaseProof purchaseProof, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

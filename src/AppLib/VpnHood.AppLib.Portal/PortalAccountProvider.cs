using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.AppLib.Abstractions.Billing;
using VpnHood.AppLib.Portal.Dto;
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.AppLib.Portal;

/// <summary>
/// Account facade over the Portal API. One read (GET /account) answers the whole account —
/// identity, THE one access code serving it and the store subscription behind it — because the
/// portal ranks and chooses server-side; no device walks a list, and no backend id is ever on
/// the wire.
/// </summary>
public class PortalAccountProvider : IAccountProvider, IDisposable
{
    private readonly IAuthenticationProvider _authenticationProvider;
    private readonly IBillingProvider? _billingProvider;
    private readonly string _packageName;

    // Which store this build sells through, taken from the billing provider rather than passed in
    // beside it: the provider cannot be wrong about which store it is, and a second statement of
    // the same fact is one that can disagree. Null means this build has no store at all.
    private readonly string? _storeId;

    // This provider owns its transport: base address, TLS policy and lifetime are decided here, and
    // the credential is asked for per call. Handing a ready-made client between components is what
    // used to make those three someone else's decision.
    private readonly HttpClient _httpClient;

    public PortalAccountProvider(
        IAuthenticationProvider authenticationProvider,
        IBillingProvider? billingProvider,
        Uri portalBaseUrl,
        string packageName,
        bool ignoreSslVerification = false)
    {
        _authenticationProvider = authenticationProvider;
        _billingProvider = billingProvider;
        _storeId = billingProvider?.ProviderId;
        _packageName = packageName;

        var handler = new HttpClientHandler();
        if (ignoreSslVerification) handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        _httpClient = new HttpClient(handler) { BaseAddress = portalBaseUrl };

        Billing = billingProvider != null
            ? new AppBilling {
                Provider = billingProvider,
                OrderProcessor = new PortalOrderProcessor(_httpClient, authenticationProvider,
                    billingProvider.ProviderId, packageName)
            }
            : null;
    }

    public IAuthenticationProvider AuthenticationProvider => _authenticationProvider;

    public AppBilling? Billing { get; }

    /// <summary>
    /// The sellable products according to the portal (GET /billing/products), which is where the
    /// mapping from a store product to a plan already lives: a product the portal does not map
    /// cannot be redeemed, so asking it — rather than trusting the build's own list — is what keeps
    /// a purchase from landing on a plan the backend has never heard of. Read anonymously: the
    /// resource takes no session, and a plans page renders before anyone signs in.
    /// <para>
    /// A portal that cannot answer is a failure, not an empty catalog, and it is deliberately not
    /// softened with the build's own ids: the payment sheet would still open, the store would still
    /// charge, and the proof would then have nowhere to be redeemed. The UI turns this into "the
    /// store is unavailable, try again", which is the only honest offer while the backend is down.
    /// An answered-but-empty catalog is a different thing and is honoured as given — the portal
    /// saying "nothing is sellable here" is an answer.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<string>> GetProductIds(CancellationToken cancellationToken)
    {
        var storeId = _storeId
            ?? throw new InvalidOperationException("This build has no store, so it sells nothing.");

        // no authentication provider passed: the catalog takes no session, so no token is fetched
        // and no 401 here could ever be mistaken for a dead one
        var apiClient = new PortalApiClient(_httpClient);
        var productIds = await apiClient.ListProducts(storeId, _packageName, cancellationToken).Vhc();
        if (productIds.Count == 0)
            VhLogger.Instance.LogWarning(
                "The portal maps no sellable product for this app. StoreId: {StoreId}, PackageName: {PackageName}",
                storeId, _packageName);

        return productIds;
    }

    public async Task<Account?> GetAccount(CancellationToken cancellationToken)
    {
        if (AuthenticationProvider.UserId == null)
            return null;

        // The wire maps the app model 1:1 and arrives fully ranked (the portal chose THE one access
        // code, the app never picks). The single fact the portal cannot know is composed here:
        // whether this device can manage the subscription, which needs both that this build's store
        // billed it and that the store app on this device can show the screen — a cross-store
        // subscription is managed where it was bought, and the UI says so.
        var apiClient = new PortalApiClient(_httpClient, _authenticationProvider);
        var account = await apiClient.GetAccount(cancellationToken).Vhc();
        if (account.Subscription != null)
            account.Subscription.Management = ResolveManagement(account.Subscription.StoreId);
        return account;
    }

    private SubscriptionManagement ResolveManagement(string subscriptionStoreId)
    {
        // A store this build does not ship to billed it — bought on Android, now signed in on an
        // iPhone. Nothing here can manage it, and nothing may name the store that can.
        if (_billingProvider == null || subscriptionStoreId != _billingProvider.ProviderId)
            return SubscriptionManagement.AnotherStore;

        return _billingProvider.IsSubscriptionManagementSupported
            ? SubscriptionManagement.Available
            : SubscriptionManagement.NotOnThisDevice;
    }

    public Task SetAccessCode(string? accessCode, CancellationToken cancellationToken)
    {
        var apiClient = new PortalApiClient(_httpClient, _authenticationProvider);
        return apiClient.SetAccessCode(accessCode, cancellationToken);
    }

    public Task ReportAccessCodeRejected(string accessCode, CancellationToken cancellationToken)
    {
        var apiClient = new PortalApiClient(_httpClient, _authenticationProvider);
        return apiClient.ReportAccessCodeRejected(accessCode, cancellationToken);
    }

    public Task DeleteAccount(CancellationToken cancellationToken)
    {
        // The portal erases the person and every session with them; this device is signed out by
        // AccountService once this returns, which is also what drops the external IdP's cached
        // credential so the next sign-in is a deliberate act creating a brand-new account.
        var apiClient = new PortalApiClient(_httpClient, _authenticationProvider);
        return apiClient.DeleteAccount(cancellationToken);
    }

    public void Dispose()
    {
        Billing?.Provider.Dispose();
        _authenticationProvider.Dispose();
        _httpClient.Dispose();
    }
}

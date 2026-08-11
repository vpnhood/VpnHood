using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.AppLib.Portal;

/// <summary>
/// The sellable products according to the portal (GET /billing/plans), which is where the mapping from
/// a store product to a plan already lives: a product the portal does not map cannot be redeemed, so
/// asking it — rather than the build's own list — is what keeps a purchase from landing on a plan the
/// backend has never heard of.
/// <para>
/// The embedded ids stand in whenever the portal cannot answer (offline, first run, an older module):
/// a backend outage must not empty the plans page. An answered-but-empty catalog is honoured as given —
/// the portal saying "nothing is sellable here" is an answer, and falling back there would offer
/// products no payment could be redeemed against.
/// </para>
/// </summary>
/// <remarks>
/// The client carries no session: this resource takes none, and an app must render its plans page
/// before anyone signs in.
/// </remarks>
internal class PortalProductCatalog(
    HttpClient httpClient,
    string storeId,
    string packageName,
    IReadOnlyList<string> fallbackProductIds)
    : IAppProductCatalog
{
    public async Task<IReadOnlyList<string>> GetProductIds(CancellationToken cancellationToken)
    {
        try {
            var apiClient = new PortalApiClient(httpClient);
            var plans = await apiClient.ListPlans(storeId, packageName, cancellationToken).Vhc();

            // a store product may carry several plans (Play base plans); the store is queried per product
            var productIds = plans.Items.Select(x => x.StoreProductId).Distinct().ToArray();
            if (productIds.Length == 0)
                VhLogger.Instance.LogWarning(
                    "The portal maps no sellable product for this app. Store: {Store}, PackageName: {PackageName}",
                    storeId, packageName);

            return productIds;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex,
                "Could not read the product catalog from the portal; falling back to the embedded ids. " +
                "Store: {Store}, PackageName: {PackageName}", storeId, packageName);
            return fallbackProductIds;
        }
    }
}

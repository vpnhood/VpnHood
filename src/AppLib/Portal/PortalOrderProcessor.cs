using System.Security.Authentication;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.AppLib.Abstractions.Billing;
using VpnHood.AppLib.Portal.Dto;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.AppLib.Portal;

/// <summary>
/// Portal reconciliation: one synchronous POST /billing/purchases returns the
/// entitlement and access code — no polling loop (that was Store.Server's
/// design). A short retry only covers the store-side "pending" state.
/// </summary>
internal class PortalOrderProcessor(
    HttpClient httpClient,
    IAuthenticationProvider authenticationProvider,
    string storeId,
    string packageName)
    : IOrderProcessor
{
    public Task<PurchaseAttribution> PreparePurchase(CancellationToken cancellationToken)
    {
        // The portal's external uid is a UUID by contract, which is what lets every store take it as
        // it is — Apple only accepts a UUID. Reshaping it is the billing provider's business.
        var userId = authenticationProvider.UserId
            ?? throw new AuthenticationException("Could not prepare the purchase because the user is not signed in.");

        return Task.FromResult(new PurchaseAttribution { UserId = userId });
    }

    public async Task CompleteOrder(PurchaseProof purchaseProof, CancellationToken cancellationToken)
    {
        var apiClient = new PortalApiClient(httpClient, authenticationProvider);
        for (var counter = 0; ; counter++) {
            var state = await apiClient
                .CreatePurchase(storeId, packageName, purchaseProof.Value, cancellationToken).Vhc();

            if (state == PortalPurchaseState.Provisioned)
                return;

            // store-side payment not complete yet — brief retry, then give up loudly
            VhLogger.Instance.LogWarning("Purchase is still pending at the store. Attempt: {Attempt}", counter + 1);
            if (counter == 2)
                throw new InvalidOperationException("The store has not completed the payment yet. Try again later.");
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).Vhc();
        }
    }
}

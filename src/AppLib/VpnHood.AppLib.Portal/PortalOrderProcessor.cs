using System.Security.Authentication;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Portal.Dto;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.AppLib.Portal;

/// <summary>
/// Portal reconciliation: one synchronous purchase.verify call returns the
/// entitlement and access code — no polling loop (that was Store.Server's
/// design). A short retry only covers the store-side "pending" state.
/// </summary>
internal class PortalOrderProcessor(
    PortalAuthenticationProvider authenticationProvider,
    string storeId,
    string packageName)
    : IAppOrderProcessor
{
    public Task<AppPurchaseAttribution> PreparePurchase(CancellationToken cancellationToken)
    {
        // the portal's external uid is a UUID: GooglePlay obfuscatedAccountId
        // AND (as a Guid) the Apple appAccountToken — the backend owns this mapping
        var userId = authenticationProvider.UserId
            ?? throw new AuthenticationException("Could not prepare the purchase because the user is not signed in.");

        return Task.FromResult(new AppPurchaseAttribution {
            AccountId = userId,
            AppAccountToken = Guid.TryParse(userId, out var appAccountToken) ? appAccountToken : null
        });
    }

    public async Task CompleteOrder(AppPurchaseResult purchaseResult, CancellationToken cancellationToken)
    {
        var purchaseData = purchaseResult.PurchaseData
            ?? throw new InvalidOperationException("The store purchase carries no proof for verification.");

        var apiClient = new PortalApiClient(authenticationProvider.HttpClient);
        for (var counter = 0; ; counter++) {
            var entitlement = await apiClient.Invoke<PortalEntitlement>("purchase.verify",
                new Dictionary<string, object?> {
                    ["store"] = storeId,
                    ["packageName"] = packageName,
                    ["proof"] = new Dictionary<string, object?> { ["purchaseToken"] = purchaseData }
                }, cancellationToken).Vhc();

            switch (entitlement.State) {
                case PortalEntitlement.StateProvisioned:
                    return;

                case PortalEntitlement.StateAwaitingEmailVerification:
                    // parked server-side; resumes on verification — surface it, don't spin
                    throw new InvalidOperationException(
                        "Your email address must be verified before the purchase can be delivered. " +
                        "Please check your inbox and try again.");

                case PortalEntitlement.StatePending:
                    // store-side payment not complete yet — brief retry, then give up loudly
                    VhLogger.Instance.LogWarning("Purchase is still pending at the store. Attempt: {Attempt}", counter + 1);
                    if (counter == 2)
                        throw new InvalidOperationException("The store has not completed the payment yet. Try again later.");
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).Vhc();
                    break;

                default:
                    throw new InvalidOperationException($"Unexpected entitlement state: {entitlement.State}");
            }
        }
    }
}

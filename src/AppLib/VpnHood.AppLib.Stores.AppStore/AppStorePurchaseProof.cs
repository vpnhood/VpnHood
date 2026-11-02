using VpnHood.AppLib.Abstractions.Billing;

namespace VpnHood.AppLib.Stores.AppStore;

// The proof with the transaction it came from, so FinishPurchase can finish that transaction once
// the portal has redeemed the JWS; to everyone else it is a plain proof.
internal sealed class AppStorePurchaseProof : PurchaseProof
{
    public required string TransactionId { get; init; }
}

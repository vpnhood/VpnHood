using Android.BillingClient.Api;
using VpnHood.AppLib.Abstractions.Billing;
using PurchaseState = Android.BillingClient.Api.PurchaseState;

namespace VpnHood.AppLib.Droid.GooglePlay.Exceptions;

/// <summary>
/// Translates Play Billing results into the store-agnostic <see cref="BillingException" />.
/// The store's own DebugMessage travels as the exception message (and Data["StoreMessage"]);
/// the UI branches only on the mapped <see cref="BillingErrorCode" />, never on Play's codes.
/// </summary>
internal static class GoogleBillingErrors
{
    public static BillingException Create(BillingResult billingResult, PurchaseState? purchaseState = null)
    {
        // The one legal Ok in here: Play answers Ok with no order id while the payment settles,
        // and the UI must be told "pending", not handed a failure.
        if (purchaseState == PurchaseState.Pending)
            return new BillingException(BillingErrorCode.Pending,
                "The store has not settled the payment yet.");

        if (billingResult.ResponseCode == BillingResponseCode.Ok)
            throw new InvalidOperationException("Response code should not be OK.");

        var code = billingResult.ResponseCode switch {
            BillingResponseCode.UserCancelled => BillingErrorCode.Cancelled,
            BillingResponseCode.BillingUnavailable => BillingErrorCode.Unavailable,
            BillingResponseCode.FeatureNotSupported => BillingErrorCode.Unavailable,
            BillingResponseCode.NetworkError => BillingErrorCode.NetworkError,
            BillingResponseCode.ServiceDisconnected => BillingErrorCode.NetworkError,
            BillingResponseCode.ServiceTimeout => BillingErrorCode.NetworkError,
            BillingResponseCode.ServiceUnavailable => BillingErrorCode.NetworkError,
            BillingResponseCode.ItemUnavailable => BillingErrorCode.ProductUnavailable,
            BillingResponseCode.ItemAlreadyOwned => BillingErrorCode.AlreadyOwned,
            BillingResponseCode.ItemNotOwned => BillingErrorCode.NotOwned,
            _ => BillingErrorCode.Unknown
        };

        var message = string.IsNullOrWhiteSpace(billingResult.DebugMessage)
            ? $"Google Play billing failed. ResponseCode: {billingResult.ResponseCode}"
            : billingResult.DebugMessage;

        return new BillingException(code, message);
    }

    public static BillingException PlayUnavailable() =>
        new(BillingErrorCode.Unavailable, "Google Play is unavailable on this device.");
}

using VpnHood.AppLib.Abstractions.Billing;

namespace VpnHood.AppLib.Ios.AppStore.Exceptions;

/// <summary>
/// Translates StoreKit outcomes into the store-agnostic <see cref="BillingException" />, the
/// App Store twin of GoogleBillingErrors. The native bridge has no error-code channel — failures
/// arrive as plain exceptions with a message string — so only the recognized purchase states get
/// specific codes and everything else maps to <see cref="BillingErrorCode.Unknown" />; the
/// store's message still travels with the exception for display and logs, it just never becomes
/// a branch.
/// </summary>
internal static class AppStoreBillingErrors
{
    public static BillingException Cancelled() =>
        new(BillingErrorCode.Cancelled, "The purchase was cancelled in the App Store sheet.");

    public static BillingException Pending() =>
        new(BillingErrorCode.Pending,
            "The purchase is awaiting approval (Ask to Buy). It will be delivered once approved.");

    public static BillingException Wrap(Exception ex) =>
        new(BillingErrorCode.Unknown, ex.Message, ex);
}

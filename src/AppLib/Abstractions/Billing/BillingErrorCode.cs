namespace VpnHood.AppLib.Abstractions.Billing;

/// <summary>
/// The store-agnostic vocabulary for billing failures. Providers translate their store's own
/// error codes into these; the UI branches on nothing finer. Anything a store can say that has
/// no user-meaningful bucket here is <see cref="Unknown" /> — the store's own message still
/// travels with the exception for display and logs, it just never becomes a branch.
/// </summary>
public enum BillingErrorCode
{
    Unknown,

    /// <summary>The person closed the store's payment flow; not an error to show.</summary>
    Cancelled,

    /// <summary>The store accepted the order but the payment has not settled yet.</summary>
    Pending,

    /// <summary>Billing does not work on this device or store account at all (unsupported
    /// device/region, store app missing or outdated, policy restriction).</summary>
    Unavailable,

    /// <summary>Transient trouble reaching the store's billing service; retrying can help.</summary>
    NetworkError,

    /// <summary>The chosen product cannot be bought right now (not in this store/region).</summary>
    ProductUnavailable,

    /// <summary>The store account already owns this subscription.</summary>
    AlreadyOwned,

    /// <summary>The store account owns no such purchase.</summary>
    NotOwned
}

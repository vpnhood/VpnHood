namespace VpnHood.AppLib.Abstractions.Billing;

/// <summary>
/// The store's own evidence that a purchase happened — a Play purchase token, an App Store signed
/// transaction (JWS). A pointer, not a claim: the backend presents it back to the store's API and
/// believes the store's answer, never this string.
/// <para>
/// A type rather than a bare string because it is what <see cref="IBillingProvider" /> answers, and
/// a return value has no parameter name to say which of a purchase's several strings is meant. The
/// order id is the tempting wrong one, and it fails at the backend rather than here.
/// </para>
/// </summary>
public class PurchaseProof
{
    /// <summary>
    /// The store's string, verbatim. Opaque to the whole app: nobody between the store and the
    /// backend parses it, and which wire field carries it is the account backend's business.
    /// </summary>
    public required string Value { get; init; }
}

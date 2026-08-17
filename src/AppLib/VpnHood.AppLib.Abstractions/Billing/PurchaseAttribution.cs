namespace VpnHood.AppLib.Abstractions.Billing;

/// <summary>
/// What the store must be told before it takes a payment, so the order comes back tied to its
/// buyer — and so a store token stolen from one device cannot be redeemed into another account.
/// <para>
/// A type rather than a bare string because it is what <see cref="IOrderProcessor" /> answers and
/// what <see cref="IBillingProvider" /> is then handed: on neither side is there a parameter or
/// property name to say which of a purchase's several ids is meant, so the type says it.
/// </para>
/// </summary>
public class PurchaseAttribution
{
    /// <summary>
    /// The signed-in account's id. Every store asks for the same thing under its own name — Google's
    /// obfuscatedAccountId, Apple's appAccountToken, which must be a UUID — so shaping it is each
    /// billing provider's job, not this one's.
    /// </summary>
    public required string UserId { get; init; }
}

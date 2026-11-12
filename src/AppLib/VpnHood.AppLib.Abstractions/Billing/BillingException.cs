namespace VpnHood.AppLib.Abstractions.Billing;

/// <summary>
/// A store billing failure in store-agnostic vocabulary. Every <see cref="IBillingProvider" />
/// translates its store's own errors into a <see cref="BillingErrorCode" /> here, so nothing
/// above the provider — the app, the client API, the UI — ever learns which store failed or
/// how that store spells its errors. Data carries the code and the store's own message across
/// the client API (the UI reads Data["BillingErrorCode"] / Data["StoreMessage"]).
/// </summary>
public sealed class BillingException : Exception
{
    /// <summary>Gets the failure's code.</summary>
    public BillingErrorCode Code { get; }

    /// <summary>Initializes a new instance of the <see cref="BillingException"/> class.</summary>
    /// <param name="code">The failure's code.</param>
    /// <param name="message">The store's own message.</param>
    /// <param name="innerException">The store's error, or <see langword="null"/>.</param>
    public BillingException(BillingErrorCode code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        Data["BillingErrorCode"] = code.ToString();
        Data["StoreMessage"] = message;
    }
}

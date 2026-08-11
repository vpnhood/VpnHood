namespace VpnHood.AppLib.Abstractions;

/// <summary>
/// The in-app purchase capability of an account provider. Purchasing always needs both sides:
/// the store that collects the payment and the backend that reconciles the order and delivers access.
/// Which is also why the backend owns the catalog: a product it cannot map is a payment it cannot
/// turn into access.
/// </summary>
public sealed class AppBilling
{
    /// <summary>The store: prices the products, collects the payment.</summary>
    public required IAppBillingProvider Provider { get; init; }

    /// <summary>The backend: turns a completed purchase into delivered access.</summary>
    public required IAppOrderProcessor OrderProcessor { get; init; }

    /// <summary>The backend: which store products this app may sell.</summary>
    public required IAppProductCatalog ProductCatalog { get; init; }
}

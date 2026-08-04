namespace VpnHood.AppLib.Abstractions;

/// <summary>
/// The in-app purchase capability of an account provider. Purchasing always needs both sides:
/// the store that collects the payment and the backend that reconciles the order and delivers access.
/// </summary>
public sealed class AppBilling
{
    public required IAppBillingProvider Provider { get; init; }
    public required IAppOrderProcessor OrderProcessor { get; init; }
}

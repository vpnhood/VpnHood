using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.AppLib.Abstractions;

public interface IAppAccountProvider
{
    IAppAuthenticationProvider AuthenticationProvider { get; }

    /// <summary>Null when the app has no in-app purchasing.</summary>
    AppBilling? Billing { get; }

    Task<AppAccount?> GetAccount(CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ListAccessKeys(string subscriptionId, CancellationToken cancellationToken);
    Task<string> GetAccessCode(string subscriptionId, CancellationToken cancellationToken);

    /// <summary>
    /// Delete the signed-in account everywhere ("forget me"): the backend erases the person, every
    /// device is signed out, and a later sign-in creates a brand-new account. Never cancels a store
    /// subscription and never ends already-paid access — the store's own lifecycle does. Mandatory,
    /// not optional: any provider with sign-in owes an in-app deletion path by store policy
    /// (Apple 5.1.1(v), Google Play).
    /// </summary>
    Task DeleteAccount(IUiContext uiContext, CancellationToken cancellationToken);
}

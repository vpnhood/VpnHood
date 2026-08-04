namespace VpnHood.AppLib.Abstractions;

public interface IAppAccountProvider
{
    IAppAuthenticationProvider AuthenticationProvider { get; }

    /// <summary>Null when the app has no in-app purchasing.</summary>
    AppBilling? Billing { get; }

    Task<AppAccount?> GetAccount(CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ListAccessKeys(string subscriptionId, CancellationToken cancellationToken);
    Task<string> GetAccessCode(string subscriptionId, CancellationToken cancellationToken);
}

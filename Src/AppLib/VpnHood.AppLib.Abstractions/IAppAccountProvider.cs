namespace VpnHood.AppLib.Abstractions;

public interface IAppAccountProvider
{
    IAppAuthenticationProvider AuthenticationProvider { get; }
    IAppBillingProvider? BillingProvider { get; }
    Task<AppAccount?> GetAccount(CancellationToken cancellationToken);
    Task<string[]> ListAccessKeys(string subscriptionId, CancellationToken cancellationToken);
}
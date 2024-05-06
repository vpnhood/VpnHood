namespace VpnHood.Client.App.Abstractions;

public interface IAppAccountService
{
    IAppAuthenticationService Authentication { get; }
    IAppBillingService? Billing { get; }
    Task<AppAccount?> GetAccount();
    Task Refresh();
    Task<bool> IsSubscriptionOrderProcessed(string providerOrderId);
    Task<string[]> GetAccessKeys(string subscriptionId);
}
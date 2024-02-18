namespace VpnHood.Client.App.Abstractions;

public interface IAppAccountService
{
    IAppAuthenticationService Authentication { get; }
    IAppBillingService? Billing { get; }
    Task<AppAccount?> GetAccount();
    Task<bool> IsSubscriptionOrderProcessed(string providerOrderId);
    Task<List<string>> GetAccessKeys(string subscriptionId);
}
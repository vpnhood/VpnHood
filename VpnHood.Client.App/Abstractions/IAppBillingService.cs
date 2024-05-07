namespace VpnHood.Client.App.Abstractions;

public interface IAppBillingService : IDisposable
{
    Task<SubscriptionPlan[]> GetSubscriptionPlans();
    
    /// <returns>Provider Order Id</returns>
    Task<string> Purchase(IAppUiContext uiContext, string planId);

    string? PurchaseState { get; }
}
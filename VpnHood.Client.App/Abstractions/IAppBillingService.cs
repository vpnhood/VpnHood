namespace VpnHood.Client.App.Abstractions;

public interface IAppBillingService : IDisposable
{
    Task<SubscriptionPlan[]> GetSubscriptionPlans();
    Task<string> Purchase(IAppUiContext uiContext, string planId);
}
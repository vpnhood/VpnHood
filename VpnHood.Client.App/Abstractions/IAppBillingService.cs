namespace VpnHood.Client.App.Abstractions;

public interface IAppBillingService : IDisposable
{
    Task<SubscriptionPlan[]> GetSubscriptionPlans();
    Task Purchase(string userId, string planId);
}
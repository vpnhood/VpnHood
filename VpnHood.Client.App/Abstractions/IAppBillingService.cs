namespace VpnHood.Client.App.Abstractions;

public interface IAppBillingService
{
    Task<SubscriptionPlan[]> GetSubscriptionPlans();
}
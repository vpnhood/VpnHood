using VpnHood.Client.App.Abstractions;

namespace VpnHood.Client.App.WebServer.Api;

public interface IBillingController
{
    Task<SubscriptionPlan[]> GetSubscriptionPlans();
}
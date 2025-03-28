using System.Diagnostics.CodeAnalysis;
using VpnHood.AppLib.Abstractions;

namespace VpnHood.AppLib.WebServer.Api;

[SuppressMessage("ReSharper", "UnusedMember.Global")]
public interface IBillingController
{
    Task<SubscriptionPlan[]> GetSubscriptionPlans();
    Task<string> Purchase(string planId);
    Task<AppPurchaseOptions> GetPurchaseOptions();
}
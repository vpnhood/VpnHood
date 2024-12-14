using System.Diagnostics.CodeAnalysis;
using VpnHood.AppFramework.Abstractions;

namespace VpnHood.AppFramework.WebServer.Api;

[SuppressMessage("ReSharper", "UnusedMember.Global")]
public interface IBillingController
{
    Task<SubscriptionPlan[]> GetSubscriptionPlans();
    Task<string> Purchase(string planId);
}
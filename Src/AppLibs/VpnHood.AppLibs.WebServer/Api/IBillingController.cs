using System.Diagnostics.CodeAnalysis;
using VpnHood.AppLibs.Abstractions;

namespace VpnHood.AppLibs.WebServer.Api;

[SuppressMessage("ReSharper", "UnusedMember.Global")]
public interface IBillingController
{
    Task<SubscriptionPlan[]> GetSubscriptionPlans();
    Task<string> Purchase(string planId);
}
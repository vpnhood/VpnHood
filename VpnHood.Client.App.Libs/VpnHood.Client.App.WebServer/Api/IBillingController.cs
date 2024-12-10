using System.Diagnostics.CodeAnalysis;
using VpnHood.Client.App.Abstractions;

namespace VpnHood.Client.App.WebServer.Api;

[SuppressMessage("ReSharper", "UnusedMember.Global")]
public interface IBillingController
{
    Task<SubscriptionPlan[]> GetSubscriptionPlans();
    Task<string> Purchase(string planId);
}
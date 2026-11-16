using VpnHood.AppLogic.Abstractions.Billing;
using VpnHood.Net.Toolkit.ApiClients;

namespace VpnHood.AppLogic.App.Services.Accounts;

internal class StoreInfo
{
    public static StoreInfo Empty => new() { StoreError = null, SubscriptionPlans = [] };
    public required IReadOnlyList<SubscriptionPlan> SubscriptionPlans { get; init; }
    public required ApiError? StoreError { get; init; }
    public bool IsAvailable => SubscriptionPlans.Count > 0;
}

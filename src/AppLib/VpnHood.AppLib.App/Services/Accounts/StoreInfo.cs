using VpnHood.AppLib.Abstractions.Billing;
using VpnHood.Core.Toolkit.ApiClients;

namespace VpnHood.AppLib.Services.Accounts;

public class StoreInfo
{
    public static StoreInfo Empty => new() { StoreError = null, SubscriptionPlans = [] };
    public required IReadOnlyList<SubscriptionPlan> SubscriptionPlans { get; init; }
    public required ApiError? StoreError { get; init; }
    public bool IsAvailable => SubscriptionPlans.Count > 0;
}

using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Toolkit.ApiClients;

namespace VpnHood.AppLib.Services.Accounts;

public class AppStoreInfo
{
    public static AppStoreInfo Empty => new() { StoreError = null, StoreName = null, SubscriptionPlans = [] };
    public required string? StoreName { get; init; }
    public required SubscriptionPlan[] SubscriptionPlans { get; init; }
    public required ApiError? StoreError { get; init; }
    public bool IsAvailable => SubscriptionPlans.Length > 0;
}

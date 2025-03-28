using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Toolkit.ApiClients;

namespace VpnHood.AppLib;

public class AppPurchaseOptions
{
    public required string? StoreName { get; init; }
    public required ApiError? StoreError { get; init; }
    public required SubscriptionPlan[] SubscriptionPlans { get; init; }
    public required Uri? PurchaseUrl { get; init; }
}
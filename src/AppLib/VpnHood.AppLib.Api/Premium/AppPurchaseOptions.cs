using VpnHood.AppLib.Api.Billing;
using VpnHood.Net.Toolkit.ApiClients;

namespace VpnHood.AppLib.Api.Premium;

public class AppPurchaseOptions
{
    public required bool IsStoreAvailable { get; init; }
    public required ApiError? StoreError { get; init; }
    public required IReadOnlyList<SubscriptionPlan> SubscriptionPlans { get; init; }
    public required Uri? PurchaseUrl { get; init; }
    public required bool CanGoPremiumByCode { get; init; }
}
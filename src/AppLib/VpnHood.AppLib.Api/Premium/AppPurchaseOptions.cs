using VpnHood.AppLib.Api.Billing;
using VpnHood.Net.Toolkit.ApiClients;

namespace VpnHood.AppLib.Api.Premium;

/// <summary>
/// Represents where the person can buy a premium plan for a VPN profile: the in-app store's plans, the server
/// operator's own shop, or a premium code they type.
/// </summary>
/// <remarks>
/// It offers one purchase surface: where the operator names its own shop and the build may open one, the in-app store is
/// not asked.
/// </remarks>
public class AppPurchaseOptions
{
    /// <summary>Gets a value indicating whether the in-app store has plans to sell.</summary>
    public required bool IsStoreAvailable { get; init; }

    /// <summary>Gets why the in-app store could not be read, or <see langword="null"/>.</summary>
    public required ApiError? StoreError { get; init; }

    /// <summary>Gets the in-app store's plans, as the store prices them; none where it was not asked or has none.</summary>
    public required IReadOnlyList<SubscriptionPlan> SubscriptionPlans { get; init; }

    /// <summary>
    /// Gets the server operator's own shop, or <see langword="null"/> where it names none or the build may not open one,
    /// as a store's build.
    /// </summary>
    public required Uri? PurchaseUrl { get; init; }

    /// <summary>
    /// Gets a value indicating whether the person may go premium by typing a premium code: the server's policy offers
    /// it, and the build takes typed codes.
    /// </summary>
    public required bool CanGoPremiumByCode { get; init; }
}
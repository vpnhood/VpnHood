using VpnHood.AppLib.Api.Billing;
using VpnHood.AppLib.App.Services.Accounts;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.AppLib.App.DtoConverters;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.AppLib.Api;

namespace VpnHood.AppLib.App.ApiImpl;

internal sealed class BillingApi(VpnHoodApp app) : IBillingApi
{
    private BillingService BillingService =>
        app.Services.AccountService?.BillingService ??
        throw new Exception("Billing service is not available at this moment.");

    public async Task<IReadOnlyList<SubscriptionPlan>> GetSubscriptionPlans(CancellationToken cancellationToken)
    {
        var plans = await BillingService.GetSubscriptionPlans(cancellationToken).Vhc();
        return plans.Select(x => x.ToAppDto()).ToArray();
    }

    // the window this process has, if any: the desktop service never has one, and a store that
    // needs one says so
    public Task Purchase(PurchaseParams purchaseParams, CancellationToken cancellationToken)
    {
        return BillingService.Purchase(AppUiContext.Context, purchaseParams.ToProvider(), cancellationToken);
    }

    public Task<bool> RestorePurchase(CancellationToken cancellationToken)
    {
        return BillingService.RestorePurchase(AppUiContext.Context, cancellationToken);
    }

    public Task OpenSubscriptionManagement(CancellationToken cancellationToken)
    {
        return BillingService.OpenSubscriptionManagement(AppUiContext.Context, cancellationToken);
    }
}
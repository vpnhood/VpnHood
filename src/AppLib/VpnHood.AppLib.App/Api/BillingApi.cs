using VpnHood.AppLib.Api.Billing;
using VpnHood.AppLib.Services.Accounts;
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.AppLib.DtoConverters;
using VpnHood.Core.Toolkit.Extensions;

namespace VpnHood.AppLib.Api;

internal sealed class BillingApi(VpnHoodApp app) : IBillingApi
{
    private BillingService BillingService =>
        app.Services.AccountService?.BillingService ??
        throw new Exception("Billing service is not available at this moment.");

    public async Task<IReadOnlyList<SubscriptionPlan>> GetSubscriptionPlans(CancellationToken cancellationToken)
    {
        var plans = await BillingService.GetSubscriptionPlans(cancellationToken).Vhc();
        return [.. plans.Select(x => x.ToAppDto())];
    }

    public Task Purchase(PurchaseParams purchaseParams, CancellationToken cancellationToken)
    {
        return BillingService.Purchase(AppUiContext.RequiredContext, purchaseParams.ToProvider(), cancellationToken);
    }

    public Task<bool> RestorePurchase(CancellationToken cancellationToken)
    {
        return BillingService.RestorePurchase(AppUiContext.RequiredContext, cancellationToken);
    }

    public Task OpenSubscriptionManagement(CancellationToken cancellationToken)
    {
        return BillingService.OpenSubscriptionManagement(AppUiContext.RequiredContext, cancellationToken);
    }
}
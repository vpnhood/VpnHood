using Microsoft.AspNetCore.Mvc;
using VpnHood.AppLib.Abstractions.Billing;
using VpnHood.AppLib.Api;
using VpnHood.AppLib.Api.SwaggerHost.Exceptions;
using VpnHood.AppLib.Contracts.Premium;

namespace VpnHood.AppLib.Api.SwaggerHost.Controllers;

[ApiController]
[Route("api/billing")]
public class BillingController : ControllerBase, IBillingApi
{
    [HttpGet("subscription-plans")]
    public Task<IReadOnlyList<SubscriptionPlan>> GetSubscriptionPlans(CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("purchase")]
    public Task Purchase(PurchaseParams purchaseParams, CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("restore-purchase")]
    public Task<bool> RestorePurchase(CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("subscription-management")]
    public Task OpenSubscriptionManagement(CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpGet("purchase-options")]
    public Task<AppPurchaseOptions> GetPurchaseOptions(Guid clientProfileId, CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }
}
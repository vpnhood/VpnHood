using Microsoft.AspNetCore.Mvc;
using VpnHood.AppLib.Abstractions.Billing;
using VpnHood.AppLib.Swagger.Exceptions;
using VpnHood.AppLib.WebServer.Api;

namespace VpnHood.AppLib.Swagger.Controllers;

[ApiController]
[Route("api/billing")]
public class BillingController : ControllerBase, IBillingController
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
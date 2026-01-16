using Microsoft.AspNetCore.Mvc;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Swagger.Exceptions;
using VpnHood.AppLib.WebServer.Api;

namespace VpnHood.AppLib.Swagger.Controllers;

[ApiController]
[Route("api/billing")]
public class BillingController : ControllerBase, IBillingController
{
    [HttpGet("subscription-plans")]
    public Task<SubscriptionPlan[]> GetSubscriptionPlans(CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("purchase")]
    public Task<string> Purchase(PurchaseParams purchaseParams, CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }

    [HttpGet("purchase-options")]
    public Task<AppPurchaseOptions> GetPurchaseOptions(CancellationToken cancellationToken)
    {
        throw new SwaggerOnlyException();
    }
}
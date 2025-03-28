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
    public Task<SubscriptionPlan[]> GetSubscriptionPlans()
    {
        throw new SwaggerOnlyException();
    }

    [HttpPost("purchase")]
    public Task<string> Purchase(string planId)
    {
        throw new SwaggerOnlyException();
    }

    public Task<AppPurchaseOptions> GetPurchaseOptions()
    {
        throw new SwaggerOnlyException();
    }
}
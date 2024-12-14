using Microsoft.AspNetCore.Mvc;
using VpnHood.AppFramework.Abstractions;
using VpnHood.AppFramework.Swagger.Exceptions;
using VpnHood.AppFramework.WebServer.Api;

namespace VpnHood.AppFramework.Swagger.Controllers;

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
}
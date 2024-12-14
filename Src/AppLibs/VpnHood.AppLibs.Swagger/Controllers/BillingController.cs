using Microsoft.AspNetCore.Mvc;
using VpnHood.AppLibs.Abstractions;
using VpnHood.AppLibs.Swagger.Exceptions;
using VpnHood.AppLibs.WebServer.Api;

namespace VpnHood.AppLibs.Swagger.Controllers;

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
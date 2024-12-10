using Microsoft.AspNetCore.Mvc;
using VpnHood.Client.App.Abstractions;
using VpnHood.Client.App.Swagger.Exceptions;
using VpnHood.Client.App.WebServer.Api;

namespace VpnHood.Client.App.Swagger.Controllers;

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
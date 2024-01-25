using Microsoft.AspNetCore.Mvc;
using VpnHood.Client.App.Abstractions;
using VpnHood.Client.App.WebServer.Api;

namespace VpnHood.Client.App.Swagger.Controllers;

[ApiController]
[Route("api/billing")]
public class BillingController : ControllerBase, IBillingController
{
    [HttpGet("subscription-plans")]
    public Task<SubscriptionPlan[]> GetSubscriptionPlans()
    {
        throw new NotImplementedException();
    }
}
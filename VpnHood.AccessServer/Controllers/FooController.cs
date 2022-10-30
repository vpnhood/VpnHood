using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.MultiLevelAuthorization.Services;
using VpnHood.AccessServer.Persistence;

namespace VpnHood.AccessServer.Controllers;

[Route("/api/foo")]
[AllowAnonymous]
public class FooController : SuperController<FooController>
{
    public FooController(ILogger<FooController> logger, VhContext vhContext, MultilevelAuthService multilevelAuthService) 
        : base(logger, vhContext, multilevelAuthService)
    {
    }

    [HttpGet]
    public IActionResult Get()
    {
        Logger.LogInformation("aaaa1");
        Logger.LogWarning("aaaa1");
        return new JsonResult(Request.Headers["HOSTNAME"]);
    }
}
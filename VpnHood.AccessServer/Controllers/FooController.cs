using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Controllers;

[Route("/api/foo")]
[AllowAnonymous]
public class FooController : SuperController<FooController>
{
    public FooController(ILogger<FooController> logger, VhContext vhContext) 
        : base(logger, vhContext)
    {
    }

    [HttpGet]
    public IActionResult Get()
    {
        return new JsonResult(Request.Headers["HOSTNAME"]);
    }
}
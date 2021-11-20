using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace VpnHood.AccessServer.Controllers
{
    [Route("/api/foo")]
    [AllowAnonymous]
    public class FooController : SuperController<FooController>
    {
        public FooController(ILogger<FooController> logger) 
            : base(logger)
        {
        }

        [HttpGet]
        public IActionResult Get()
        {
            return new JsonResult(Request.Headers["HOSTNAME"]);
        }
    }
}
using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Logging;

namespace VpnHood.AccessServer.Controllers
{
    [Route("/api/foo")]
    public class FooController : SuperController<FooController>
    {
        public FooController(ILogger<FooController> logger) 
            : base(logger)
        {
        }

        [HttpGet]
        public IActionResult Get()
        {
            VhLogger.Instance.LogInformation("Zigma");
            Console.WriteLine("Zapool");
            return new JsonResult(AccessServerApp.Instance.AppLocalDataPath);
        }
    }
}
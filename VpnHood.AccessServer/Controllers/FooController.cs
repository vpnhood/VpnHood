using System;
using System.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using VpnHood.Logging;

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
            return new JsonResult(AccessServerApp.Instance.AppDataPath);
        }

    }
}
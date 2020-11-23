using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace VpnHood.AccessServer.Controllers
{
    [ApiController]
    [Route("")]
    public class ApiController : SuperController<ApiController>
    {
        public ApiController(ILogger<ApiController> logger) : base(logger)
        {
        }

        [HttpGet]
        public string Get()
        {
            var str = $"{Assembly.GetExecutingAssembly().GetName().Name} is running!\nVersion: {Assembly.GetExecutingAssembly().GetName().Version}";
            return str;
        }
    }
}

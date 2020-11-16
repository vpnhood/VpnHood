using System.Reflection;
using Microsoft.AspNetCore.Mvc;

namespace VpnHood.AccessServer.Controllers
{
    [ApiController]
    [Route("")]
    public class ApiController : ControllerBase
    {
        [HttpGet]
        public string Get()
        {
            var str = $"{Assembly.GetExecutingAssembly().GetName().Name} is running!\nVersion: {Assembly.GetExecutingAssembly().GetName().Version}";
            return str;
        }
    }
}

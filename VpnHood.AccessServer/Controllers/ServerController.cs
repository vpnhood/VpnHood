using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [Authorize(AuthenticationSchemes = "auth", Roles = "Admin")]
    public class ServerController : SuperController<ServerController>
    {
        public ServerController(ILogger<ServerController> logger) : base(logger)
        {
        }

        [HttpGet]
        [Route(nameof(Get))]
        public Task<Models.Server> Get(Guid serverId)
        {
            using VhContext vhContext = new();
            return vhContext.Servers.SingleAsync(e => e.ServerId == serverId);
        }
    }
}

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
    public class ClientController : SuperController<ClientController>
    {
        public ClientController(ILogger<ClientController> logger) : base(logger)
        {
        }

        [HttpGet]
        [Route(nameof(Get))]
        public Task<Client> Get(Guid clientId)
        {
            using VhContext vhContext = new();
            return vhContext.Clients.SingleAsync(e => e.ClientId == clientId);
        }
    }
}

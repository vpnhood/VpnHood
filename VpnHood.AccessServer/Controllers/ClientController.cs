using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Controllers
{
    [Route("/api/projects/{projectId}/clients")]
    [Authorize(AuthenticationSchemes = "auth", Roles = "Admin")]
    public class ClientController : SuperController<ClientController>
    {
        public ClientController(ILogger<ClientController> logger) : base(logger)
        {
        }

        [HttpGet("{clientId}")]
        public async Task<Client> Get(Guid projectId, Guid clientId)
        {
            await using VhContext vhContext = new();
            return await vhContext.Clients.SingleAsync(x => x.ProjectId == projectId && x.UserClientId == clientId);
        }
    }
}

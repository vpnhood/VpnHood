using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Controllers
{
    [Route("/api/projects/{projectId:guid}/clients")]
    [Authorize(AuthenticationSchemes = "auth", Roles = "Admin")]
    public class ClientController : SuperController<ClientController>
    {
        public ClientController(ILogger<ClientController> logger) : base(logger)
        {
        }

        [HttpGet("{clientId:guid}")]
        public async Task<ProjectClient> Get(Guid projectId, Guid clientId)
        {
            await using VhContext vhContext = new();
            return await vhContext.ProjectClients.SingleAsync(x => x.ProjectId == projectId && x.ClientId == clientId);
        }

        [HttpGet]
        public async Task<ProjectClient[]> List(Guid projectId)
        {
            await using VhContext vhContext = new();
            var query = vhContext.ProjectClients.Where(x => x.ProjectId == projectId);

            return await query.ToArrayAsync();
        }
    }
}
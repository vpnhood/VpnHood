using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Controllers
{
    [Route("/api/projects/{projectId:guid}/clients")]
    public class ClientController : SuperController<ClientController>
    {
        public ClientController(ILogger<ClientController> logger) : base(logger)
        {
        }

        [HttpGet("{clientId:guid}")]
        public async Task<ProjectClient> Get(Guid projectId, Guid clientId)
        {
            await using var vhContext = new VhContext();
            return await vhContext.ProjectClients.SingleAsync(x => x.ProjectId == projectId && x.ClientId == clientId);
        }

        [HttpGet]
        public async Task<ProjectClient[]> List(Guid projectId)
        {
            await using var vhContext = new VhContext();
            var query = vhContext.ProjectClients.Where(x => x.ProjectId == projectId);

            return await query.ToArrayAsync();
        }
    }
}
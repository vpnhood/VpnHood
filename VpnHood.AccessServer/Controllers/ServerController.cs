using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.DTOs;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Controllers
{
    [Route("/api/projects/{projectId}/servers")]
    [Authorize(AuthenticationSchemes = "auth", Roles = "Admin")]
    public class ServerController : SuperController<ServerController>
    {
        public ServerController(ILogger<ServerController> logger) : base(logger)
        {
        }

        [HttpGet("{serverId}")]
        public async Task<ServerData> Get(Guid projectId, Guid serverId)
        {
            await using VhContext vhContext = new();
            var query = from S in vhContext.Servers
                join SSL in vhContext.ServerStatusLogs on new {key1 = S.ServerId, key2 = true} equals new
                    {key1 = SSL.ServerId, key2 = SSL.IsLast} into grouping
                from SSL in grouping.DefaultIfEmpty()
                where S.ProjectId == projectId && S.ServerId == serverId
                select new ServerData {Server = S, Status = SSL};

            return await query.SingleAsync();
        }

        [HttpGet("{serverId}/StatusLogs")]
        public async Task<ServerStatusLog[]> GetStatusLogs(Guid projectId, Guid serverId, int recordIndex = 0,
            int recordCount = 1000)
        {
            await using VhContext vhContext = new();

            var list = await vhContext.ServerStatusLogs
                .Include(x => x.Server)
                .Where(x => x.Server!.ProjectId == projectId && x.Server.ServerId == serverId)
                .OrderByDescending(x => x.ServerStatusLogId)
                .Skip(recordIndex).Take(recordCount)
                .ToArrayAsync();

            return list;
        }
    }
}
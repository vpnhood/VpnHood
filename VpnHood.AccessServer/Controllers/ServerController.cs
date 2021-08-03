using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Models;
using System.Linq;

namespace VpnHood.AccessServer.Controllers
{
    [ApiController]
    [Route("{projectId}/[controller]s")]
    [Authorize(AuthenticationSchemes = "auth", Roles = "Admin")]
    public class ServerController : SuperController<ServerController>
    {
        public ServerController(ILogger<ServerController> logger) : base(logger)
        {
        }

        public class ServerData
        {
            public Models.Server Server { get; set; }
            public ServerStatusLog Status { get; set; }
        }

        [HttpGet]
        [Route("{serverId}")]
        public async Task<ServerData> Get(Guid projectId, Guid serverId)
        {
            using VhContext vhContext = new();
            var query = from S in vhContext.Servers
                        join SSL in vhContext.ServerStatusLogs on new { key1 = S.ServerId, key2 = true } equals new { key1 = SSL.ServerId, key2 = SSL.IsLast } into grouping
                        from SSL in grouping.DefaultIfEmpty()
                        where S.ProjectId == projectId && S.ServerId == serverId
                        select new ServerData { Server = S, Status = SSL };

            return await query.SingleAsync();
        }

        [HttpGet]
        [Route("{serverId}/statusLogs")]
        public async Task<ServerStatusLog[]> GetStatusLogs(Guid projectId, Guid serverId, int recordIndex = 0, int recordCount = 1000)
        {
            using VhContext vhContext = new();

            var list = await vhContext.ServerStatusLogs
                .Include(x => x.Server)
                .Where(x => x.Server.ProjectId == projectId && x.Server.ServerId == serverId)
                .OrderByDescending(x => x.ServerStatusLogId)
                .Skip(recordIndex).Take(recordCount)
                .ToArrayAsync();

             return list;
        }
    }
}

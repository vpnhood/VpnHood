using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.DTOs;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Controllers
{
    [Route("/api/projects/{projectId}/servers")]
    public class ServerController : SuperController<ServerController>
    {
        public ServerController(ILogger<ServerController> logger) : base(logger)
        {
        }

        [HttpPost]
        public async Task<Models.Server> Create(Guid projectId, ServerCreateParams createParams)
        {
            await using VhContext vhContext = new();
            var server = new Models.Server
            {
                ProjectId = projectId,
                ServerId = Guid.NewGuid(),
                CreatedTime = DateTime.UtcNow,
                ServerName = createParams.ServerName,
            };
            await vhContext.Servers.AddAsync(server);
            await vhContext.SaveChangesAsync();
            return server;

        }

        [HttpGet("{serverId:guid}")]
        public async Task<ServerData> Get(Guid projectId, Guid serverId)
        {
            await using VhContext vhContext = new();
            var query = from s in vhContext.Servers
                join ssl in vhContext.ServerStatusLogs on new {key1 = s.ServerId, key2 = true} equals new
                    {key1 = ssl.ServerId, key2 = ssl.IsLast} into grouping
                from ssl in grouping.DefaultIfEmpty()
                where s.ProjectId == projectId && s.ServerId == serverId
                select new ServerData {Server = s, Status = ssl};

            return await query.SingleAsync();
        }

        [HttpGet("{serverId:guid}/status-logs")]
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

        [HttpGet]
        public async Task<ServerData[]> List(Guid projectId, int recordIndex = 0, int recordCount = 1000)
        {
            await using VhContext vhContext = new();
            var query = from server in vhContext.Servers
                join serverStatusLog in vhContext.ServerStatusLogs on new { key1 = server.ServerId, key2 = true } equals new
                    { key1 = serverStatusLog.ServerId, key2 = serverStatusLog.IsLast } into grouping
                from serverStatusLog in grouping.DefaultIfEmpty()
                where server.ProjectId == projectId
                select new ServerData { Server = server, Status = serverStatusLog };

            var res = await query
                .Skip(recordIndex)
                .Take(recordCount)
                .ToArrayAsync();

            return res;
        }
        
    }
}
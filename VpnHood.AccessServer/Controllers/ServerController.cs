using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.DTOs;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Security;
using VpnHood.Common;

namespace VpnHood.AccessServer.Controllers
{
    [Route("/api/projects/{projectId:guid}/servers")]
    public class ServerController : SuperController<ServerController>
    {
        public ServerController(ILogger<ServerController> logger) : base(logger)
        {
        }

        [HttpPost]
        public async Task<Models.Server> Create(Guid projectId, ServerCreateParams createParams)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.WriteServer);

            var server = new Models.Server
            {
                ProjectId = projectId,
                ServerId = Guid.NewGuid(),
                CreatedTime = DateTime.UtcNow,
                ServerName = createParams.ServerName,
                Secret = Util.GenerateSessionKey(),
                AuthorizationCode = Guid.NewGuid()
            };
            await vhContext.Servers.AddAsync(server);
            await vhContext.SaveChangesAsync();

            server.Secret = Array.Empty<byte>();
            return server;
        }

        [HttpGet("{serverId:guid}")]
        public async Task<ServerData> Get(Guid projectId, Guid serverId)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.ReadServer);

            var query = from server in vhContext.Servers
                        join serverStatusLog in vhContext.ServerStatusLogs on new { key1 = server.ServerId, key2 = true } equals new
                        { key1 = serverStatusLog.ServerId, key2 = serverStatusLog.IsLast } into grouping
                        from serverStatusLog in grouping.DefaultIfEmpty()
                        where server.ProjectId == projectId && server.ServerId == serverId
                        select new ServerData { Server = server, Status = serverStatusLog };

            var serverData = await query.SingleAsync();
            serverData.Server.Secret = Array.Empty<byte>();
            return serverData;
        }

        [HttpGet("{serverId:guid}/status-logs")]
        public async Task<ServerStatusLog[]> GetStatusLogs(Guid projectId, Guid serverId, int recordIndex = 0,
            int recordCount = 1000)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.ReadServer);

            var list = await vhContext.ServerStatusLogs
                .Include(x => x.Server)
                .Where(x => x.Server!.ProjectId == projectId && x.Server.ServerId == serverId)
                .OrderByDescending(x => x.ServerStatusLogId)
                .Skip(recordIndex).Take(recordCount)
                .ToArrayAsync();

            return list;
        }

        [HttpGet("{serverId:guid}/config")]
        public async Task<string> GetConfig(Guid projectId, Guid serverId)
        {
            // ::1  443   udp: -
            // ::1  256   udp: -

            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.ReadServerConfig);

            var server = await vhContext.Servers.SingleAsync(x => x.ProjectId == projectId && x.ServerId == serverId);
            var authItem = AccessServerApp.Instance.RobotAuthItem; 

            var claims = new List<Claim>
            {
                new("authorization_code", server.AuthorizationCode.ToString())
            };

            // create jwt
            var jwt = JwtTool.CreateSymmetricJwt(Convert.FromBase64String(authItem.SymmetricSecurityKey!), authItem.Issuers[0], authItem.ValidAudiences[0], serverId.ToString(), claims.ToArray());
            var serverConfig = new 
            {
                ServerId = serverId,
                RestBaseUrl = Request.Headers.ContainsKey("HOSTNAME") ? new Uri(Request.Headers["HOSTNAME"]) : null,
                RestAuthorization = $"Bearer {jwt}"
            };

            var config = JsonSerializer.Serialize(serverConfig, new JsonSerializerOptions { WriteIndented = true });
            return config;
        }


        [HttpGet]
        public async Task<ServerData[]> List(Guid projectId, int recordIndex = 0, int recordCount = 1000)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.ReadServer);

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

            // remove server secret
            foreach (var item in res)
                item.Server.Secret = Array.Empty<byte>();

            return res;
        }

    }
}
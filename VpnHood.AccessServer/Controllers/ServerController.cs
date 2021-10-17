using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mime;
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
            await VerifyUserPermission(vhContext, projectId, Permissions.ServerWrite);

            // validate
            var accessControlGroup = await vhContext.AccessPointGroups.SingleOrDefaultAsync(x =>
                x.ProjectId == projectId &&
                x.AccessPointGroupId == createParams.AccessPointGroupId);

            var server = new Models.Server
            {
                ProjectId = projectId,
                ServerId = Guid.NewGuid(),
                CreatedTime = DateTime.UtcNow,
                ServerName = createParams.ServerName,
                Secret = Util.GenerateSessionKey(),
                AuthorizationCode = Guid.NewGuid(),
                AccessPointGroupId = accessControlGroup?.AccessPointGroupId
            };
            await vhContext.Servers.AddAsync(server);
            await vhContext.SaveChangesAsync();

            server.Secret = Array.Empty<byte>();
            return server;
        }

        [HttpPatch]
        public Task<Models.Server> Update(Guid projectId, ServerUpdateParams createParams)
        {
            throw new NotImplementedException();
        }


        [HttpGet("{serverId:guid}")]
        public async Task<ServerData> Get(Guid projectId, Guid serverId)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.ServerRead);

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
            await VerifyUserPermission(vhContext, projectId, Permissions.ServerRead);

            var list = await vhContext.ServerStatusLogs
                .Include(x => x.Server)
                .Where(x => x.Server!.ProjectId == projectId && x.Server.ServerId == serverId)
                .OrderByDescending(x => x.ServerStatusLogId)
                .Skip(recordIndex).Take(recordCount)
                .ToArrayAsync();

            return list;
        }

        [HttpGet("{serverId:guid}/appsettings")]
        [Produces(MediaTypeNames.Text.Plain)]
        public async Task<string> GetAppSettingsJson(Guid projectId, Guid serverId)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.ServerReadConfig);

            var server = await vhContext.Servers.SingleAsync(x => x.ProjectId == projectId && x.ServerId == serverId);
            var authItem = AccessServerApp.Instance.RobotAuthItem;

            var claims = new List<Claim>
            {
                new("authorization_code", server.AuthorizationCode.ToString())
            };

            // create jwt
            var jwt = JwtTool.CreateSymmetricJwt(Convert.FromBase64String(authItem.SymmetricSecurityKey!), 
                authItem.Issuers[0], authItem.ValidAudiences[0], serverId.ToString(), claims.ToArray());

            var port = Request.Host.Port ?? (Request.IsHttps ? 443 : 80);
            var uri = new UriBuilder(Request.Scheme, Request.Host.Host, port, "/api/agent/").Uri;
            var agentAppSettings = new AgentAppSettings(uri, $"Bearer {jwt}", server.Secret);

            var config = JsonSerializer.Serialize(agentAppSettings, new JsonSerializerOptions { WriteIndented = true });
            return config;
        }


        [HttpGet]
        public async Task<ServerData[]> List(Guid projectId, int recordIndex = 0, int recordCount = 1000)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.ServerRead);

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
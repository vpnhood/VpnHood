using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.DTOs;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Security;

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
            await VerifyUserPermission(vhContext, projectId, Permissions.ClientRead);

            return await vhContext.ProjectClients.SingleAsync(x => x.ProjectId == projectId && x.ClientId == clientId);
        }

        [HttpGet]
        public async Task<ProjectClientData[]> List(Guid projectId,
            Guid? deviceId = null, Guid? serverId = null, Guid? accessId = null,
            Guid? accessTokenId = null, Guid? accessPointGroupId = null,
            DateTime? startTime = null, DateTime? endTime = null, int recordIndex = 0, int recordCount = 300)
        {
            startTime ??= DateTime.MinValue;
            endTime ??= DateTime.UtcNow;

            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.ClientRead);

            // select and order
            var q =
                from accessUsage in vhContext.AccessUsages
                join session in vhContext.Sessions on accessUsage.SessionId equals session.SessionId
                join access in vhContext.Accesses on session.AccessId equals access.AccessId
                join accessToken in vhContext.AccessTokens on session.AccessTokenId equals accessToken.AccessTokenId
                join server in vhContext.Servers on session.ServerId equals server.ServerId
                join projectClient in vhContext.ProjectClients on session.ProjectClientId equals projectClient.ProjectClientId
                where server.ProjectId == projectId && accessUsage.CreatedTime >= startTime && accessUsage.CreatedTime <= endTime
                select new { accessUsage, session.ProjectClientId, session.SessionId, session.ServerId, accessUsage.AccessId, accessToken.AccessTokenId, accessToken.AccessPointGroupId };

            if (accessTokenId != null) q = q.Where(x => x.AccessTokenId == accessTokenId);
            if (accessPointGroupId != null) q = q.Where(x => x.AccessPointGroupId == accessPointGroupId);
            if (serverId != null) q = q.Where(x => x.ServerId == serverId);
            if (accessId != null) q = q.Where(x => x.AccessId == accessId);
            if (deviceId != null) q = q.Where(x => x.ProjectClientId == deviceId);

            // group by accessId
            var query1 = q
                .GroupBy(x => x.ProjectClientId)
                .Select(x => new
                {
                    ProjectClientId = x.Key,
                    LastAccessUsageId = x.Select(y => y.accessUsage.AccessUsageId).Max(),
                    Usage = new Usage
                    {
                        ClientCount = 1,
                        SentTraffic = x.Sum(y => y.accessUsage.SentTraffic),
                        ReceivedTraffic = x.Sum(y => y.accessUsage.ReceivedTraffic),
                        LastTime = x.Max(y => y.accessUsage.CreatedTime),
                        ServerCount = x.Select(y => y.ServerId).Distinct().Count(),
                        SessionCount = x.Select(y => y.SessionId).Distinct().Count(),
                        AccessCount = x.Select(y => y.AccessId).Distinct().Count()
                    }
                });

            query1 = query1
                .OrderByDescending(x => x.Usage.LastTime)
                .Skip(recordIndex)
                .Take(recordCount);

            // create output
            var query2 = from projectClient in vhContext.ProjectClients
                         join usage in query1 on projectClient.ProjectClientId equals usage.ProjectClientId
                         join accessUsage in vhContext.AccessUsages
                            .Include(x => x.Access)
                            .Include(x => x.Server)
                            .Include(x => x.Session)
                            .Include(x => x.Session!.AccessToken)
                            .Include(x => x.Session!.AccessToken!.AccessPointGroup)
                         on usage.LastAccessUsageId equals accessUsage.AccessUsageId
                         select new ProjectClientData
                         {
                             Usage = usage.Usage,
                             LastAccessUsage = accessUsage,
                         };

            vhContext.DebugMode = true; //todo
            var res = await query2.ToArrayAsync();

            return res;
        }

    }
}
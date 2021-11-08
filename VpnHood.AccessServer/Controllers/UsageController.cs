using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Security;

namespace VpnHood.AccessServer.Controllers
{
    [Route("/api/projects/{projectId:guid}/usages")]
    public class UsageController : SuperController<UsageController>
    {
        public UsageController(ILogger<UsageController> logger) : base(logger)
        {
        }
        public class UsageData
        {
            public long SentTraffic { get; set; }
            public long ReceivedTraffic { get; set; }
            public long CycleSentTraffic { get; set; }
            public long CycleReceivedTraffic { get; set; }
            public long TotalReceivedTraffic { get; set; }
            public long TotalSentTraffic { get; set; }
        }

        [HttpGet]

        public  async Task<UsageData[]> List(Guid projectId,Guid? accessTokenId = null, Guid? accessPointGroupId = null, 
            DateTime? startTime = null, DateTime? endTime = null,
            int recordIndex = 0, int recordCount = 1000)
        {
            startTime ??= DateTime.MinValue;
            endTime ??= DateTime.UtcNow;

            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.AccessTokenRead);

            // select and order
            var q =
                from usage in vhContext.AccessUsages
                join session in vhContext.Sessions on usage.SessionId equals session.SessionId
                join accessToken in vhContext.AccessTokens on session.AccessTokenId equals accessToken.AccessTokenId
                join server in vhContext.Servers on session.ServerId equals server.ServerId
                where server.ProjectId == projectId && usage.CreatedTime >= startTime && usage.CreatedTime <= endTime
                select new { usage, usage.AccessId, accessToken.AccessTokenId, accessToken.AccessPointGroupId };

            if (accessTokenId != null) q = q.Where(x => x.AccessTokenId == accessTokenId);
            if (accessTokenId != null) q = q.Where(x => x.AccessPointGroupId == accessPointGroupId);
            q = q.OrderByDescending(x => x.usage.AccessUsageId);
            
            // group by accessId
            var query = q.GroupBy(x=>x.AccessId).Select(x=>new UsageData
            {
                SentTraffic = x.Sum(y=>y.usage.SentTraffic),
                ReceivedTraffic = x.Sum(y=>y.usage.ReceivedTraffic),
                CycleSentTraffic = x.First().usage.CycleSentTraffic,
                CycleReceivedTraffic = x.First().usage.CycleReceivedTraffic,
                TotalReceivedTraffic = x.First().usage.TotalReceivedTraffic,
                TotalSentTraffic = x.First().usage.TotalSentTraffic
            });

            var res = await query
                .Skip(recordIndex)
                .Take(recordCount)
                .ToArrayAsync();

            return res;
        }

        [HttpGet("{accessTokenId:guid}/usage")]
        public async Task<Access> GetAccess(Guid projectId, Guid accessTokenId, Guid? clientId = null)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.AccessTokenRead);

            return await vhContext.Accesses
                .Include(x => x.ProjectClient)
                .Include(x => x.AccessToken)
                .Where(x => x.AccessToken!.ProjectId == projectId &&
                            x.AccessToken.AccessTokenId == accessTokenId &&
                            (!x.AccessToken.IsPublic || x.ProjectClient!.ClientId == clientId))
                .SingleOrDefaultAsync();
        }

        [HttpGet("{accessTokenId:guid}/usage-logs")]
        public async Task<AccessUsage[]> GetAccessLogs(Guid projectId, Guid? accessTokenId = null,
            Guid? clientId = null, int recordIndex = 0, int recordCount = 1000)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.AccessTokenRead);

            var query = vhContext.AccessUsages
                .Include(x => x.Server)
                .Include(x => x.Session)
                .Include(x => x.Session!.Access)
                .Include(x => x.Session!.Client)
                .Include(x => x.Session!.Access!.AccessToken)
                .Where(x => x.Session!.Client!.ProjectId == projectId &&
                            x.Server != null &&
                            x.Session.Client != null &&
                            x.Session != null &&
                            x.Session.Access != null &&
                            x.Session.Access.AccessToken != null);

            if (accessTokenId != null)
                query = query
                    .Where(x => x.Session!.Access!.AccessTokenId == accessTokenId);

            if (clientId != null)
                query = query
                    .Where(x => x.Session!.Client!.ClientId == clientId);

            var res = await query
                .OrderByDescending(x => x.AccessUsageId)
                .Skip(recordIndex).Take(recordCount)
                .ToArrayAsync();

            return res;
        }

        [HttpDelete("{accessTokenId:guid}")]
        public async Task Delete(Guid projectId, Guid accessTokenId)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.AccessTokenWrite);

            var accessToken = await vhContext.AccessTokens
                .SingleAsync(x => x.ProjectId == projectId && x.AccessTokenId == accessTokenId);

            vhContext.AccessTokens.Remove(accessToken);
            await vhContext.SaveChangesAsync();
        }
    }
}
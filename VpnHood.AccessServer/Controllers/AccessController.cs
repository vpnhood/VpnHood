using System;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.DTOs;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Security;

namespace VpnHood.AccessServer.Controllers
{
    [Route("/api/projects/{projectId:guid}/accesses")]
    public class AccessController : SuperController<AccessController>
    {
        public AccessController(ILogger<AccessController> logger) : base(logger)
        {
        }

        [HttpGet]
        public async Task<AccessData[]> List(Guid projectId,
            Guid? accessTokenId = null, Guid? accessPointGroupId = null, Guid? deviceId = null,
            DateTime? startTime = null, DateTime? endTime = null, int recordIndex = 0, int recordCount = 300)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.AccessTokenWrite);

            var hasStartTime = startTime != null;
            var hasEndTime = endTime != null && endTime < DateTime.UtcNow.AddHours(-1);

            // calculate usage
            var query1 =
                from accessUsage in vhContext.AccessUsages
                join access in vhContext.Accesses on accessUsage.AccessId equals access.AccessId
                join session in vhContext.Sessions on accessUsage.SessionId equals session.SessionId
                join accessToken in vhContext.AccessTokens on session.AccessTokenId equals accessToken.AccessTokenId
                where accessToken.ProjectId == projectId &&
                        (accessTokenId == null || accessToken.AccessTokenId == accessTokenId) &&
                        (accessPointGroupId == null || accessToken.AccessPointGroupId == accessPointGroupId) &&
                        (deviceId == null || session.DeviceId == deviceId) &&
                        (startTime == null || accessUsage.CreatedTime >= startTime) &&
                        (endTime == null || accessUsage.CreatedTime <= endTime)
                group new { session, accessUsage, access, accessToken } by accessUsage.AccessId into g
                select new 
                {
                    AccessId = g.Key,
                    Usage = new Usage
                    {
                        LastTime = g.Max(x => x.accessUsage.CreatedTime),
                        SentTraffic = g.Sum(x => x.accessUsage.SentTraffic),
                        ReceivedTraffic = g.Sum(x => x.accessUsage.ReceivedTraffic),
                        ServerCount = g.Select(x => x.session.ServerId).Distinct().Count(),
                        DeviceCount = g.Select(x => x.session.DeviceId).Distinct().Count(),
                    }
                };

            // filter
            query1 = query1
                .Skip(recordIndex)
                .Take(recordCount);

            var query2 =
                from access in vhContext.Accesses
                join accessUsage in vhContext.AccessUsages on new { key1 = access.AccessId, key2 = true } equals new {key1=accessUsage.AccessId, key2=accessUsage.IsLast}
                join grouping in query1 on access.AccessId equals grouping.AccessId
                select new AccessData
                {
                    Access = access,
                    AccessUsage = accessUsage,
                    Usage = grouping.Usage,
                };

            var res = await query2.ToArrayAsync();
            return res;
        }



    }
}
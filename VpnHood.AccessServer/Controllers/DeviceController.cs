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
    public class DeviceController : SuperController<DeviceController>
    {
        public DeviceController(ILogger<DeviceController> logger) : base(logger)
        {
        }

        [HttpGet("{clientId:guid}")]
        public async Task<Device> Get(Guid projectId, Guid clientId)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.ClientRead);

            return await vhContext.Devices.SingleAsync(x => x.ProjectId == projectId && x.ClientId == clientId);
        }

        [HttpGet]
        public async Task<UsageData[]> List(Guid projectId,
            Guid? deviceId = null, Guid? serverId = null, Guid? accessId = null,
            Guid? accessTokenId = null, Guid? accessPointGroupId = null,
            DateTime? startTime = null, DateTime? endTime = null, int recordIndex = 0, int recordCount = 300)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.AccessTokenRead);

            // select and order
            var usages =
                from accessUsage in vhContext.AccessUsages
                join session in vhContext.Sessions on accessUsage.SessionId equals session.SessionId
                join accessToken in vhContext.AccessTokens on session.AccessTokenId equals accessToken.AccessTokenId
                where
                    (accessToken.ProjectId == projectId) &&
                    (accessTokenId == null || accessToken.AccessTokenId == accessTokenId) &&
                    (accessPointGroupId == null || accessToken.AccessPointGroupId == accessPointGroupId) &&
                    (deviceId == null || session.DeviceId == deviceId) &&
                    (serverId == null || session.ServerId == serverId) &&
                    (accessId == null || session.AccessId == accessId) &&
                    (startTime == null || accessUsage.CreatedTime >= startTime) &&
                    (endTime == null || accessUsage.CreatedTime <= endTime)
                group new { accessUsage, session } by session.DeviceId into g
                select new
                {
                    GroupByKeyId = g.Key,
                    LastAccessUsageId = g.Select(x => x.accessUsage.AccessUsageId).Max(),
                    Usage = new Usage
                    {
                        LastTime = g.Max(y => y.accessUsage.CreatedTime),
                        AccessCount = g.Select(y => y.accessUsage.AccessId).Distinct().Count(),
                        SessionCount = g.Select(y => y.session.SessionId).Distinct().Count(),
                        ServerCount = g.Select(y => y.session.ServerId).Distinct().Count(),
                        DeviceCount = g.Select(y => y.session.DeviceId).Distinct().Count(),
                        AccessTokenCount = g.Select(y => y.session.AccessTokenId).Distinct().Count(),
                        SentTraffic = g.Sum(y => y.accessUsage.SentTraffic),
                        ReceivedTraffic = g.Sum(y => y.accessUsage.ReceivedTraffic)
                    }
                };

            usages = usages
                .OrderByDescending(x => x.Usage.LastTime)
                .Skip(recordIndex)
                .Take(recordCount);

            // create output
            var query =
                    from usage in usages
                    join accessUsage in vhContext.AccessUsages
                        .Include(x => x.Session)
                        .Include(x => x.Session!.Device)
                        .Include(x => x.Session!.Server)
                        .Include(x => x.Session!.AccessToken)
                        .Include(x => x.Session!.AccessToken!.AccessPointGroup)
                    on usage.LastAccessUsageId equals accessUsage.AccessUsageId
                    select new UsageData
                    {
                        Usage = usage.Usage,
                        LastAccessUsage = accessUsage,
                    };

            var res = await query.ToArrayAsync();
            return res;
        }
    }
}
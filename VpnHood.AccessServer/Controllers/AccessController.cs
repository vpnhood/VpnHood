using System;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.DTOs;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Security;

namespace VpnHood.AccessServer.Controllers;

[Route("/api/projects/{projectId:guid}/accesses")]
public class AccessController : SuperController<AccessController>
{
    public AccessController(ILogger<AccessController> logger, VhContext vhContext) 
        : base(logger, vhContext)
    {
    }


    [HttpGet("{accessId:guid}/usage")]
    public async Task<AccessData> GetUsage(Guid projectId, Guid accessId, DateTime? startTime = null, DateTime? endTime = null)
    {
        var res = await GetUsages(projectId, accessId: accessId,
            startTime: startTime, endTime: endTime);
        return res.Single();
    }

    [HttpGet("usages")]
    public async Task<AccessData[]> GetUsages(Guid projectId,
        Guid? accessTokenId = null, Guid? accessPointGroupId = null, Guid? accessId = null,
        DateTime? startTime = null, DateTime? endTime = null, int recordIndex = 0, int recordCount = 300)
    {
        await VerifyUserPermission(VhContext, projectId, Permissions.ProjectRead);

        // calculate usage
        var usages =
            from accessUsage in VhContext.AccessUsages
            join session in VhContext.Sessions on accessUsage.SessionId equals session.SessionId
            join accessToken in VhContext.AccessTokens on session.AccessTokenId equals accessToken.AccessTokenId
            where
                (accessUsage.ProjectId == projectId) &&
                (accessId == null || session.AccessId == accessId) &&
                (accessTokenId == null || session.AccessTokenId == accessTokenId) &&
                (accessPointGroupId == null || accessToken.AccessPointGroupId == accessPointGroupId) &&
                (startTime == null || accessUsage.CreatedTime >= startTime) &&
                (endTime == null || accessUsage.CreatedTime <= endTime)
            group new { accessUsage, session } by (Guid?)accessUsage.AccessId into g
            select new
            {
                GroupByKeyId = g.Key,
                LastAccessUsageId = (g.Key != null) ? (long?)g.Select(x => x.accessUsage.AccessUsageId).Max() : null,
                Usage = g.Key != null ? new Usage
                {
                    LastTime = g.Max(y => y.accessUsage.CreatedTime),
                    SentTraffic = g.Sum(y => y.accessUsage.SentTraffic),
                    ReceivedTraffic = g.Sum(y => y.accessUsage.ReceivedTraffic),
                    AccessCount = g.Select(y => y.session.AccessId).Distinct().Count(),
                    SessionCount = g.Select(y => y.session.SessionId).Distinct().Count(),
                    ServerCount = g.Select(y => y.session.ServerId).Distinct().Count(),
                    DeviceCount = g.Select(y => y.session.DeviceId).Distinct().Count(),
                    AccessTokenCount = g.Select(y => y.session.AccessTokenId).Distinct().Count(),
                    CountryCount = g.Select(y => y.session.Country).Distinct().Count(),
                } : null
            };

        // create output
        var query =
            from access in VhContext.Accesses
            join accessToken in VhContext.AccessTokens on access.AccessTokenId equals accessToken.AccessTokenId
            join usage in usages on access.AccessId equals usage.GroupByKeyId into usageGrouping
            from usage in usageGrouping.DefaultIfEmpty()
            join accessUsage in VhContext.AccessUsages on usage.LastAccessUsageId equals accessUsage.AccessUsageId into accessUsageGrouping
            from accessUsage in accessUsageGrouping.DefaultIfEmpty()
            where
                (accessToken!.ProjectId == projectId) &&
                (accessId == null || access.AccessId == accessId) &&
                (accessTokenId == null || accessToken.AccessTokenId == accessTokenId) &&
                (accessPointGroupId == null || accessToken.AccessPointGroupId == accessPointGroupId)
            select new AccessData
            {
                Access = access,
                Usage = usage.Usage,
            };

        query = query
            .OrderByDescending(x => x.Access.AccessedTime)
            .Skip(recordIndex)
            .Take(recordCount);

        var res = await query.ToArrayAsync();
        return res;
    }

    [HttpGet("Search")]
    public async Task<AccessData[]> Search(Guid projectId, string pattern, int recordIndex = 0, int recordCount = 501)
    {
        await VerifyUserPermission(VhContext, projectId, Permissions.ProjectRead);

        // calculate usage
        var query =
            from access in VhContext.Accesses
            join accessToken in VhContext.AccessTokens on access.AccessTokenId equals accessToken.AccessTokenId
            join accessPointGroup in VhContext.AccessPointGroups on accessToken.AccessPointGroupId equals accessPointGroup.AccessPointGroupId
            where
                access.AccessToken!.ProjectId == projectId && (pattern == null ||
                                                               (
                                                                   access.AccessId.ToString() == pattern ||
                                                                   access.DeviceId.ToString() == pattern ||
                                                                   access.AccessTokenId.ToString() == pattern ||
                                                                   accessToken.AccessTokenName!.StartsWith(pattern) ||
                                                                   accessToken.AccessPointGroupId.ToString() == pattern ||
                                                                   accessPointGroup.AccessPointGroupName!.StartsWith(pattern)

                                                               ))
            select new AccessData
            {
                Access = access,
                AccessStatus = AccessStatus.Active
            };

        query = query
            .OrderByDescending(x => x.Access.AccessedTime)
            .Skip(recordIndex)
            .Take(recordCount);

        var res = await query.ToArrayAsync();
        return res;
    }

}
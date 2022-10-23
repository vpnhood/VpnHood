using System;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.MultiLevelAuthorization.Repos;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.DtoConverters;

namespace VpnHood.AccessServer.Controllers;

[Route("/api/projects/{projectId:guid}/accesses")]
public class AccessController : SuperController<AccessController>
{
    public AccessController(ILogger<AccessController> logger, VhContext vhContext, MultilevelAuthRepo multilevelAuthRepo)
        : base(logger, vhContext, multilevelAuthRepo)
    {
    }

    [HttpGet("{accessId:guid}")]
    public async Task<AccessData> Get(Guid projectId, Guid accessId)
    {
        await VerifyUserPermission(VhContext, projectId, Permissions.ProjectRead);
        var res = await List(projectId, accessId: accessId);
        return res.Single();
    }

    [HttpGet("usages")]
    public async Task<AccessData[]> List(Guid projectId, Guid? accessTokenId = null, Guid? accessPointGroupId = null, Guid? accessId = null,
        DateTime? startTime = null, DateTime? endTime = null,
        int recordIndex = 0, int recordCount = 300)
    {
        await VerifyUserPermission(VhContext, projectId, Permissions.ProjectRead);

        var query = VhContext.Accesses
            .Include(x=>x.Device)
            .Include(x=>x.AccessToken)
            .Where(access =>
                (access.AccessToken!.ProjectId == projectId) &&
                (access.AccessToken!.AccessPointGroupId == accessPointGroupId || accessPointGroupId == null) &&
                (access.AccessId == accessId || accessId == null) &&
                (access.AccessTokenId == accessTokenId || accessTokenId == null) &&
                (access.CreatedTime >= startTime || startTime == null) &&
                (access.CreatedTime <= endTime || endTime == null));

        query = query
            .OrderByDescending(x => x.TotalTraffic)
            .Skip(recordIndex)
            .Take(recordCount);

        var res = await query.ToArrayAsync();
        var ret = res
            .Select(x => new AccessData(AccessConverter.FromModel(x), x.AccessToken!, x.Device))
            .ToArray();
        return ret;
    }

}
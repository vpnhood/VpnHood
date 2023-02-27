using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.MultiLevelAuthorization.Services;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Security;

namespace VpnHood.AccessServer.Controllers;

[Route("/api/v{version:apiVersion}/projects/{projectId:guid}/accesses")]
public class AccessesController : SuperController<AccessesController>
{
    public AccessesController(ILogger<AccessesController> logger, VhContext vhContext, MultilevelAuthService multilevelAuthService)
        : base(logger, vhContext, multilevelAuthService)
    {
    }

    [HttpGet("{accessId:guid}")]
    public async Task<AccessData> Get(Guid projectId, Guid accessId)
    {
        await VerifyUserPermission(projectId, Permissions.ProjectRead);
        var res = await List(projectId, accessId: accessId);
        return res.Single();
    }

    [HttpGet]
    public async Task<AccessData[]> List(Guid projectId, Guid? accessTokenId = null, Guid? serverFarmId = null, Guid? accessId = null,
        DateTime? startTime = null, DateTime? endTime = null,
        int recordIndex = 0, int recordCount = 300)
    {
        await VerifyUserPermission(projectId, Permissions.ProjectRead);

        var query = VhContext.Accesses
            .Include(x => x.Device)
            .Include(x => x.AccessToken)
            .ThenInclude(x => x!.ServerFarm)
            .Where(access =>
                (access.AccessToken!.ProjectId == projectId) &&
                (access.AccessToken!.ServerFarmId == serverFarmId || serverFarmId == null) &&
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
            .Select(accessModel => new AccessData(
                accessModel.ToDto(), 
                accessModel.AccessToken!.ToDto(accessModel.AccessToken!.ServerFarm?.ServerFarmName), 
                accessModel.Device?.ToDto()))
            .ToArray();
        return ret;
    }

}
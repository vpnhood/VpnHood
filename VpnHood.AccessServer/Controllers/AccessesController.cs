using System;
using System.Linq;
using System.Threading.Tasks;
using GrayMint.Common.AspNetCore.SimpleRoleAuthorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Security;

namespace VpnHood.AccessServer.Controllers;

[ApiController]
[Authorize]
[Route("/api/v{version:apiVersion}/projects/{projectId:guid}/accesses")]
public class AccessesController : ControllerBase
{
    private readonly VhContext _vhContext;
    public AccessesController(VhContext vhContext)
    {
        _vhContext = vhContext;
    }

    [HttpGet("{accessId:guid}")]
    [AuthorizePermission(Permissions.ProjectRead)]
    public async Task<AccessData> Get(Guid projectId, Guid accessId)
    {
        var res = await List(projectId, accessId: accessId);
        return res.Single();
    }

    [HttpGet]
    [AuthorizePermission(Permissions.ProjectRead)]
    public async Task<AccessData[]> List(Guid projectId, Guid? accessTokenId = null, Guid? serverFarmId = null, Guid? accessId = null,
        DateTime? beginTime = null, DateTime? endTime = null,
        int recordIndex = 0, int recordCount = 300)
    {
        var query = _vhContext.Accesses
            .Include(x => x.Device)
            .Include(x => x.AccessToken)
            .ThenInclude(x => x!.ServerFarm)
            .Where(access =>
                (access.AccessToken!.ProjectId == projectId) &&
                (access.AccessToken!.ServerFarmId == serverFarmId || serverFarmId == null) &&
                (access.AccessId == accessId || accessId == null) &&
                (access.AccessTokenId == accessTokenId || accessTokenId == null) &&
                (access.CreatedTime >= beginTime || beginTime == null) &&
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
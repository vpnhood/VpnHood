using GrayMint.Common.Generics;
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
[Route("/api/v{version:apiVersion}/projects/{projectId}/accesses")]
public class AccessesController(VhContext vhContext) : ControllerBase
{
    [HttpGet("{accessId:guid}")]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public async Task<AccessData> Get(Guid projectId, Guid accessId)
    {
        var res = await List(projectId, accessId: accessId);
        return res.Items.Single();
    }

    [HttpGet]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public async Task<ListResult<AccessData>> List(Guid projectId, Guid? accessTokenId = null,
        Guid? serverFarmId = null, Guid? accessId = null,
        DateTime? beginTime = null, DateTime? endTime = null,
        int recordIndex = 0, int recordCount = 300)
    {
        var baseQuery = vhContext.Accesses
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

        var query = baseQuery
            .OrderByDescending(x => x.TotalTraffic)
            .Skip(recordIndex)
            .Take(recordCount);

        var res = await query.ToArrayAsync();
        var results = res
            .Select(accessModel => new AccessData(
                accessModel.ToDto(),
                accessModel.AccessToken!.ToDto(accessModel.AccessToken!.ServerFarm?.ServerFarmName),
                accessModel.Device?.ToDto()))
            .ToArray();

        var listResult = new ListResult<AccessData> {
            Items = results,
            TotalCount = results.Length < recordCount ? recordIndex + results.Length : await baseQuery.LongCountAsync()
        };

        return listResult;
    }
}
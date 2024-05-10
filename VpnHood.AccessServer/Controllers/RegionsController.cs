using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AccessServer.Dtos.Regions;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Services;

namespace VpnHood.AccessServer.Controllers;

[ApiController]
[Authorize]
[Route("/api/v{version:apiVersion}/projects/{projectId}/regions")]
public class RegionsController(RegionService regionService)
{
    [HttpPost]
    [AuthorizeProjectPermission(Permissions.ProjectWrite)]
    public Task<Region> Create(Guid projectId, RegionCreateParams regionCreateParams)
    {
        return regionService.Create(projectId, regionCreateParams);
    }

    [HttpGet("{regionId:int}")]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public Task<Region> Get(Guid projectId, int regionId)
    {
        return regionService.Get(projectId, regionId);
    }


    [HttpPatch("{regionId:int}")]
    [AuthorizeProjectPermission(Permissions.ProjectWrite)]
    public Task<Region> Update(Guid projectId, int regionId, RegionUpdateParams updateParams)
    {
        return regionService.Update(projectId, regionId, updateParams);
    }

    [HttpDelete("{regionId:int}")]
    [AuthorizeProjectPermission(Permissions.ProjectWrite)]
    public Task Delete(Guid projectId, int regionId)
    {
        return regionService.Delete(projectId, regionId);
    }

    [HttpGet]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public Task<Region[]> List(Guid projectId)
    {
        return regionService.List(projectId);
    }
}
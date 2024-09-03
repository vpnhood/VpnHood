using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AccessServer.Dtos.FarmTokenRepos;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Services;

namespace VpnHood.AccessServer.Controllers;

[ApiController]
[Authorize]
[Route("/api/v{version:apiVersion}/projects/{projectId:guid}/server-farms/{serverFarmId}/farm-token-repos")]
public class FarmTokenReposController(FarmTokenRepoService farmTokenRepoService)
{
    [HttpPost]
    [AuthorizeProjectPermission(Permissions.ServerFarmWrite)]
    public Task<FarmTokenRepo> Create(Guid projectId, Guid serverFarmId, FarmTokenRepoCreateParams createParams)
    {
        return farmTokenRepoService.Create(projectId, serverFarmId, createParams);
    }

    [HttpGet]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public Task<FarmTokenRepo[]> List(Guid projectId, Guid serverFarmId, bool checkStatus = false, CancellationToken cancellationToken = default)
    {
        return farmTokenRepoService.List(projectId, serverFarmId: serverFarmId, checkStatus: checkStatus, cancellationToken: cancellationToken);
    }

    [HttpGet("Summary")]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public Task<FarmTokenRepoSummary> GetSummary(Guid projectId, Guid serverFarmId, CancellationToken cancellationToken)
    {
        return farmTokenRepoService.GetSummary(projectId, serverFarmId, cancellationToken);
    }


    [HttpGet("{farmTokenRepoId}")]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public Task<FarmTokenRepo> Get(Guid projectId, Guid serverFarmId, string farmTokenRepoId, bool checkStatus = false, CancellationToken cancellationToken = default)
    {
        return farmTokenRepoService.Get(projectId, serverFarmId: serverFarmId, farmTokenRepoId: Guid.Parse(farmTokenRepoId), 
            checkStatus: checkStatus, cancellationToken: cancellationToken);
    }

    [HttpPatch("{farmTokenRepoId}")]
    [AuthorizeProjectPermission(Permissions.ServerFarmWrite)]
    public Task<FarmTokenRepo> Update(Guid projectId, Guid serverFarmId, string farmTokenRepoId, FarmTokenRepoUpdateParams updateParams)
    {
        return farmTokenRepoService.Update(projectId, serverFarmId, Guid.Parse(farmTokenRepoId), updateParams);
    }

    [HttpDelete("{farmTokenRepoId}")]
    [AuthorizeProjectPermission(Permissions.ServerFarmWrite)]
    public Task Delete(Guid projectId, Guid serverFarmId, string farmTokenRepoId)
    {
        return farmTokenRepoService.Delete(projectId, serverFarmId, farmTokenRepoId);
    }

}
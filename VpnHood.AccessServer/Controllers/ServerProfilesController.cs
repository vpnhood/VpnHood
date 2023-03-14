using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System;
using VpnHood.AccessServer.MultiLevelAuthorization.Services;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Security;
using System.Linq;
using VpnHood.AccessServer.Dtos.ServerProfileDtos;
using VpnHood.AccessServer.Services;

namespace VpnHood.AccessServer.Controllers;


[Route("/api/v{version:apiVersion}/projects/{projectId:guid}/server-profiles")]
public class ServerProfilesController : SuperController<ServerProfilesController>
{
    private readonly ServerProfileService _serverProfileService;

    public ServerProfilesController(
        ILogger<ServerProfilesController> logger,
        VhContext vhContext,
        ServerProfileService serverProfileService,
        MultilevelAuthService multilevelAuthService)
        : base(logger, vhContext, multilevelAuthService)
    {
        _serverProfileService = serverProfileService;
    }

    [HttpPost]
    public async Task<ServerProfile> Create(Guid projectId, ServerProfileCreateParams? createParams = null)
    {
        await VerifyUserPermission(projectId, Permissions.ProjectWrite);
        return await _serverProfileService.Create(projectId, createParams);
    }

    [HttpGet("{serverProfileId:guid}")]
    public async Task<ServerProfileData> Get(Guid projectId, Guid serverProfileId)
    {
        await VerifyUserPermission(projectId, Permissions.ProjectRead);
        var dtos = await _serverProfileService.ListWithSummary(projectId, serverProfileId: serverProfileId);

        return dtos.Single();
    }

    [HttpPatch("{serverProfileId:guid}")]
    public async Task<ServerProfile> Update(Guid projectId, Guid serverProfileId, ServerProfileUpdateParams updateParams)
    {
        await VerifyUserPermission(projectId, Permissions.ProjectWrite);
        return await _serverProfileService.Update(projectId, serverProfileId, updateParams);
    }

    [HttpDelete("{serverProfileId:guid}")]
    public async Task Delete(Guid projectId, Guid serverProfileId)
    {
        await VerifyUserPermission(projectId, Permissions.ProjectWrite);
        await _serverProfileService.Delete(projectId, serverProfileId);
    }

    [HttpGet]
    public async Task<ServerProfileData[]> List(Guid projectId, string? search = null, int recordIndex = 0, int recordCount = 101)
    {
        await VerifyUserPermission(projectId, Permissions.ProjectRead);
        return await _serverProfileService.ListWithSummary(projectId, search, recordIndex: recordIndex, recordCount: recordCount);
    }
}
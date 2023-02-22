using System;
using System.Linq;
using System.Threading.Tasks;
using GrayMint.Common.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.MultiLevelAuthorization.Services;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Services;

namespace VpnHood.AccessServer.Controllers;

[Route("/api/v{version:apiVersion}/projects/{projectId:guid}/access-point-groups")]
public class AccessPointGroupsController : SuperController<AccessPointGroupsController>
{
    private readonly ServerFarmService _serverFarmService;

    public AccessPointGroupsController(
        ILogger<AccessPointGroupsController> logger,
        VhContext vhContext, MultilevelAuthService multilevelAuthService,
        ServerFarmService serverFarmService)
        : base(logger, vhContext, multilevelAuthService)
    {
        _serverFarmService = serverFarmService;
    }

    [HttpPost]
    public async Task<AccessPointGroup> Create(Guid projectId, AccessPointGroupCreateParams? createParams)
    {
        createParams ??= new AccessPointGroupCreateParams();
        await VerifyUserPermission(projectId, Permissions.AccessPointGroupWrite);
        if (createParams.CertificateId != null)
            await VerifyUserPermission(projectId, Permissions.CertificateWrite);

        using var asyncLock = await AsyncLock.LockAsync($"CreateAccessPointGroup_{projectId}");
        return await _serverFarmService.Create(projectId, createParams);
    }

    [HttpPatch("{accessPointGroupId}")]
    public async Task<AccessPointGroup> Update(Guid projectId, Guid accessPointGroupId, AccessPointGroupUpdateParams updateParams)
    {
        await VerifyUserPermission(projectId, Permissions.AccessPointGroupWrite);
        return await _serverFarmService.Update(projectId, accessPointGroupId, updateParams);
    }

    [HttpGet("{accessPointGroupId}")]
    public async Task<ServerFarmData> Get(Guid projectId, Guid accessPointGroupId, bool includeSummary = false)
    {
        await VerifyUserPermission(projectId, Permissions.ProjectRead);
        var dtos = includeSummary
            ? await _serverFarmService.ListWithSummary(projectId, serverFarmId: accessPointGroupId)
            : await _serverFarmService.List(projectId, serverFarmId: accessPointGroupId);

        return dtos.Single();
    }

    [HttpGet]
    public async Task<ServerFarmData[]> List(Guid projectId, string? search = null, bool includeSummary = false,
        int recordIndex = 0, int recordCount = 101)
    {
        await VerifyUserPermission(projectId, Permissions.ProjectRead);

        return includeSummary
            ? await _serverFarmService.ListWithSummary(projectId, search, null, recordIndex, recordCount)
            : await _serverFarmService.List(projectId, search, null, recordIndex, recordCount);
    }

    [HttpDelete("{serverFarmId:guid}")]
    public async Task Delete(Guid projectId, Guid serverFarmId)
    {
        await VerifyUserPermission(projectId, Permissions.AccessPointGroupWrite);
        await _serverFarmService.Delete(projectId, serverFarmId);
    }
}
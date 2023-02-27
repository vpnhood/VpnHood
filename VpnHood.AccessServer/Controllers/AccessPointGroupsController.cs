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
public class ServerFarmsController : SuperController<ServerFarmsController>
{
    private readonly ServerFarmService _serverFarmService;

    public ServerFarmsController(
        ILogger<ServerFarmsController> logger,
        VhContext vhContext, MultilevelAuthService multilevelAuthService,
        ServerFarmService serverFarmService)
        : base(logger, vhContext, multilevelAuthService)
    {
        _serverFarmService = serverFarmService;
    }

    [HttpPost]
    public async Task<ServerFarm> Create(Guid projectId, ServerFarmCreateParams? createParams)
    {
        createParams ??= new ServerFarmCreateParams();
        await VerifyUserPermission(projectId, Permissions.ServerFarmWrite);
        if (createParams.CertificateId != null)
            await VerifyUserPermission(projectId, Permissions.CertificateWrite);

        using var asyncLock = await AsyncLock.LockAsync($"CreateServerFarm_{projectId}");
        return await _serverFarmService.Create(projectId, createParams);
    }

    [HttpPatch("{serverFarmId}")]
    public async Task<ServerFarm> Update(Guid projectId, Guid serverFarmId, ServerFarmUpdateParams updateParams)
    {
        await VerifyUserPermission(projectId, Permissions.ServerFarmWrite);
        return await _serverFarmService.Update(projectId, serverFarmId, updateParams);
    }

    [HttpGet("{serverFarmId}")]
    public async Task<ServerFarmData> Get(Guid projectId, Guid serverFarmId, bool includeSummary = false)
    {
        await VerifyUserPermission(projectId, Permissions.ProjectRead);
        var dtos = includeSummary
            ? await _serverFarmService.ListWithSummary(projectId, serverFarmId: serverFarmId)
            : await _serverFarmService.List(projectId, serverFarmId: serverFarmId);

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
        await VerifyUserPermission(projectId, Permissions.ServerFarmWrite);
        await _serverFarmService.Delete(projectId, serverFarmId);
    }
}
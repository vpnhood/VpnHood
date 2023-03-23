using System;
using System.Linq;
using System.Threading.Tasks;
using GrayMint.Common.AspNetCore.SimpleRoleAuthorization;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AccessServer.Dtos.ServerFarmDtos;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Services;

namespace VpnHood.AccessServer.Controllers;

[Route("/api/v{version:apiVersion}/projects/{projectId:guid}/server-farms")]
public class ServerFarmsController : ControllerBase
{
    private readonly ServerFarmService _serverFarmService;

    public ServerFarmsController(
        ServerFarmService serverFarmService)
    {
        _serverFarmService = serverFarmService;
    }

    [HttpPost]
    [AuthorizePermission(Permission.ServerFarmWrite)]
    public Task<ServerFarm> Create(Guid projectId, ServerFarmCreateParams? createParams)
    {
        createParams ??= new ServerFarmCreateParams();
        return _serverFarmService.Create(projectId, createParams);
    }

    [HttpPatch("{serverFarmId:guid}")]
    [AuthorizePermission(Permission.ServerFarmWrite)]
    public Task<ServerFarm> Update(Guid projectId, Guid serverFarmId, ServerFarmUpdateParams updateParams)
    {
        return _serverFarmService.Update(projectId, serverFarmId, updateParams);
    }

    [HttpGet("{serverFarmId:guid}")]
    [AuthorizePermission(Permission.ProjectRead)]
    public async Task<ServerFarmData> Get(Guid projectId, Guid serverFarmId, bool includeSummary = false)
    {
        var dtos = includeSummary
            ? await _serverFarmService.ListWithSummary(projectId, serverFarmId: serverFarmId)
            : await _serverFarmService.List(projectId, serverFarmId: serverFarmId);

        return dtos.Single();
    }

    [HttpGet]
    [AuthorizePermission(Permission.ProjectRead)]
    public async Task<ServerFarmData[]> List(Guid projectId, string? search = null, bool includeSummary = false,
        int recordIndex = 0, int recordCount = 101)
    {
        return includeSummary
            ? await _serverFarmService.ListWithSummary(projectId, search, null, recordIndex, recordCount)
            : await _serverFarmService.List(projectId, search, null, recordIndex, recordCount);
    }

    [HttpDelete("{serverFarmId:guid}")]
    [AuthorizePermission(Permission.ServerFarmWrite)]
    public Task Delete(Guid projectId, Guid serverFarmId)
    {
        return _serverFarmService.Delete(projectId, serverFarmId);
    }
}
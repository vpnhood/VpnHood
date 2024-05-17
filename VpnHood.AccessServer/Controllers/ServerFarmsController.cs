using System.Net.Mime;
using GrayMint.Common.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AccessServer.Dtos.Certificates;
using VpnHood.AccessServer.Dtos.ServerFarms;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Services;

namespace VpnHood.AccessServer.Controllers;

[ApiController]
[Authorize]
[Route("/api/v{version:apiVersion}/projects/{projectId:guid}/server-farms")]
public class ServerFarmsController(
    SubscriptionService subscriptionService,
    ServerFarmService serverFarmService,
    CertificateService certificateService
    ) : ControllerBase
{
    [HttpPost]
    [AuthorizeProjectPermission(Permissions.ServerFarmWrite)]
    public async Task<ServerFarmData> Create(Guid projectId, ServerFarmCreateParams? createParams)
    {
        // check user quota
        using var singleRequest = await AsyncLock.LockAsync($"{projectId}_CreateFarm");
        await subscriptionService.AuthorizeCreateServerFarm(projectId);

        createParams ??= new ServerFarmCreateParams();
        var serverFarm = await serverFarmService.Create(projectId, createParams);
        return serverFarm;
    }

    [HttpPatch("{serverFarmId:guid}")]
    [AuthorizeProjectPermission(Permissions.ServerFarmWrite)]
    public Task<ServerFarmData> Update(Guid projectId, Guid serverFarmId, ServerFarmUpdateParams updateParams)
    {
        return serverFarmService.Update(projectId, serverFarmId, updateParams);
    }

    [HttpGet("{serverFarmId:guid}")]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public Task<ServerFarmData> Get(Guid projectId, Guid serverFarmId, bool includeSummary = false)
    {
        return serverFarmService.Get(projectId, serverFarmId: serverFarmId, includeSummary: includeSummary);
    }

    [HttpGet("{serverFarmId:guid}/validate-token-url")]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public Task<ValidateTokenUrlResult> ValidateTokenUrl(Guid projectId, Guid serverFarmId, CancellationToken cancellationToken)
    {
        return serverFarmService.ValidateTokenUrl(projectId, serverFarmId: serverFarmId, cancellationToken);
    }

    [HttpGet]
    [AuthorizeProjectPermission(Permissions.ProjectRead)]
    public async Task<ServerFarmData[]> List(Guid projectId, string? search = null, bool includeSummary = false,
        int recordIndex = 0, int recordCount = 101)
    {
        return includeSummary
            ? await serverFarmService.ListWithSummary(projectId, search, null, recordIndex, recordCount)
            : await serverFarmService.List(projectId, search, null, recordIndex, recordCount);
    }

    [HttpDelete("{serverFarmId:guid}")]
    [AuthorizeProjectPermission(Permissions.ServerFarmWrite)]
    public Task Delete(Guid projectId, Guid serverFarmId)
    {
        return serverFarmService.Delete(projectId, serverFarmId);
    }

    [HttpGet("{serverFarmId:guid}/encrypted-token")]
    [AuthorizeProjectPermission(Permissions.AccessTokenReadAccessKey)]
    [Produces(MediaTypeNames.Application.Json)]
    public Task<string> GetEncryptedToken(Guid projectId, Guid serverFarmId)
    {
        return serverFarmService.GetEncryptedToken(projectId, serverFarmId);
    }

    [HttpPost("{serverFarmId:guid}/certificate/import")]
    [AuthorizeProjectPermission(Permissions.CertificateWrite)]
    public Task<Certificate> CertificateImport(Guid projectId, Guid serverFarmId, CertificateImportParams importParams)
    {
        return certificateService.Import(projectId, serverFarmId, importParams);
    }

    [HttpPost("{serverFarmId:guid}/certificate/replace")]
    [AuthorizeProjectPermission(Permissions.CertificateWrite)]
    public Task<Certificate> CertificateReplace(Guid projectId, Guid serverFarmId, CertificateCreateParams? createParams = null)
    {
        return certificateService.Replace(projectId, serverFarmId, createParams);
    }
}
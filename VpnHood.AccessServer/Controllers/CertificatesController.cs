using GrayMint.Common.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AccessServer.Dtos.Certificate;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Services;

namespace VpnHood.AccessServer.Controllers;

[ApiController]
[Route("/api/v{version:apiVersion}/projects/{projectId:guid}/")]
[Authorize]
public class CertificatesController(
    CertificateService certificateService,
    SubscriptionService subscriptionService)
    : ControllerBase
{
    [HttpPost("server-farms/{serverFarmId:guid}/certificates")]
    [AuthorizeProjectPermission(Permissions.CertificateWrite)]
    public async Task<Certificate> Create(Guid projectId, Guid serverFarmId, CertificateCreateParams? createParams = null)
    {
        using var singleRequest = await AsyncLock.LockAsync($"{projectId}_CreateCertificate");
        await subscriptionService.AuthorizeAddCertificate(projectId);

        var ret = await certificateService.Create(projectId, serverFarmId, createParams);
        return ret;
    }


    [HttpPost("server-farms/{serverFarmId:guid}/certificates/import")]
    [AuthorizeProjectPermission(Permissions.CertificateWrite)]
    public async Task<Certificate> CreateByImport(Guid projectId, Guid serverFarmId, CertificateImportParams importParams)
    {
        // check user quota
        using var singleRequest = await AsyncLock.LockAsync($"{projectId}_CreateCertificate");
        await subscriptionService.AuthorizeAddCertificate(projectId);

        var ret = await certificateService.Import(projectId, serverFarmId, importParams);
        return ret;
    }

    [HttpGet("{certificateId:guid}")]
    [AuthorizeProjectPermission(Permissions.CertificateRead)]
    public Task<Certificate> Get(Guid projectId, Guid certificateId)
    {
        return certificateService.Get(projectId, certificateId);
    }

    [HttpDelete("{certificateId}")]
    [AuthorizeProjectPermission(Permissions.CertificateWrite)]
    public Task Delete(Guid projectId, Guid certificateId)
    {
        return certificateService.Delete(projectId, certificateId);
    }

    [HttpGet]
    [AuthorizeProjectPermission(Permissions.CertificateRead)]
    public Task<IEnumerable<Certificate>> List(Guid projectId, Guid serverFarmId)
    {
        return certificateService.List(projectId, serverFarmId);
    }
}
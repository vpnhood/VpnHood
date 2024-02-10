using GrayMint.Common.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AccessServer.Dtos.Certificate;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Services;

namespace VpnHood.AccessServer.Controllers;

[ApiController]
[Route("/api/v{version:apiVersion}/projects/{projectId}/certificates")]
[Authorize]
public class CertificatesController(
    CertificateService certificateService,
    SubscriptionService subscriptionService) 
    : ControllerBase
{
    [HttpPost("self-signed")]
    [AuthorizeProjectPermission(Permissions.CertificateWrite)]
    public async Task<Certificate> CreateBySelfSigned(Guid projectId, CertificateSelfSignedParams? createParams = null)
    {
        using var singleRequest = await AsyncLock.LockAsync($"{projectId}_CreateCertificate");
        await subscriptionService.AuthorizeAddCertificate(projectId);

        var ret = await certificateService.CreateSelfSinged(projectId, createParams);
        return ret;
    }

    [HttpPost("trusted")]
    [AuthorizeProjectPermission(Permissions.CertificateWrite)]
    public async Task<Certificate> CreateTrusted(Guid projectId, CertificateSigningRequest csr)
    {
        // check user quota
        using var singleRequest = await AsyncLock.LockAsync($"{projectId}_CreateCertificate");
        await subscriptionService.AuthorizeAddCertificate(projectId);
        await subscriptionService.AuthorizeCertificateSignRequest(projectId);

        var ret = await certificateService.CreateTrusted(projectId, csr);
        return ret;
    }


    [HttpPost("import")]
    [AuthorizeProjectPermission(Permissions.CertificateWrite)]
    public async Task<Certificate> CreateByImport(Guid projectId, CertificateImportParams importParams)
    {
        // check user quota
        using var singleRequest = await AsyncLock.LockAsync($"{projectId}_CreateCertificate");
        await subscriptionService.AuthorizeAddCertificate(projectId);

        var ret = await certificateService.CreateByImport(projectId, importParams);
        return ret;
    }

    [HttpGet("{certificateId}")]
    [AuthorizeProjectPermission(Permissions.CertificateRead)]
    public Task<CertificateData> Get(Guid projectId, Guid certificateId, bool includeSummary = false)
    {
        return certificateService.Get(projectId, certificateId, includeSummary);
    }

    [HttpDelete("{certificateId}")]
    [AuthorizeProjectPermission(Permissions.CertificateWrite)]
    public Task Delete(Guid projectId, Guid certificateId)
    {
        return certificateService.Delete(projectId, certificateId);
    }

    [HttpGet]
    [AuthorizeProjectPermission(Permissions.CertificateRead)]
    public Task<IEnumerable<CertificateData>> List(Guid projectId, string? search = null, bool includeSummary = false,
        int recordIndex = 0, int recordCount = 300)
    {
        return certificateService.List(projectId, search, includeSummary: includeSummary,
            recordIndex: recordIndex, recordCount: recordCount);
    }
}
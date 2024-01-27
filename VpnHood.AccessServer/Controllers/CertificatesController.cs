using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Services;

namespace VpnHood.AccessServer.Controllers;

[ApiController]
[Route("/api/v{version:apiVersion}/projects/{projectId}/certificates")]
[Authorize]
public class CertificatesController(CertificateService certificateService) : ControllerBase
{
    [HttpPost("self-signed")]
    [AuthorizeProjectPermission(Permissions.CertificateWrite)]
    public async Task<Certificate> CreateBySelfSigned(Guid projectId, CertificateSelfSignedParams? createParams)
    {
        var ret = await certificateService.CreateSelfSinged(projectId, createParams);
        return ret;
    }

    [HttpPut("{certificateId}/self-signed")]
    [AuthorizeProjectPermission(Permissions.CertificateWrite)]
    public async Task<Certificate> ReplaceBySelfSigned(Guid projectId, Guid certificateId, CertificateSelfSignedParams? createParams = null)
    {
        var ret = await certificateService.ReplaceBySelfSinged(projectId, certificateId, createParams);
        return ret;
    }

    [HttpPost("import")]
    [AuthorizeProjectPermission(Permissions.CertificateWrite)]
    public async Task<Certificate> CreateByImport(Guid projectId, CertificateImportParams importParams)
    {
        var ret = await certificateService.CreateByImport(projectId, importParams);
        return ret;
    }

    [HttpPost("{certificateId}/import")]
    [AuthorizeProjectPermission(Permissions.CertificateWrite)]
    public async Task<Certificate> ReplaceByImport(Guid projectId, Guid certificateId, CertificateImportParams importParams)
    {
        var ret = await certificateService.ReplaceByImport(projectId, certificateId, importParams);
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
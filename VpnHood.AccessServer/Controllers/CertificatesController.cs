using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Services;

namespace VpnHood.AccessServer.Controllers;

[ApiController]
[Route("/api/v{version:apiVersion}/projects/{projectId:guid}/certificates")]
[Authorize]
public class CertificatesController : ControllerBase
{
    private readonly CertificateService _certificateService;
    public CertificatesController(CertificateService certificateService)
    {
        _certificateService = certificateService;
    }

    [HttpPost("self-signed")]
    [AuthorizeProjectPermission(Permissions.CertificateWrite)]
    public async Task<Certificate> CreateBySelfSigned(Guid projectId, CertificateSelfSignedParams? createParams)
    {
        var ret = await _certificateService.CreateSelfSinged(projectId, createParams);
        return ret;
    }

    [HttpPut("{certificateId:guid}/self-signed")]
    [AuthorizeProjectPermission(Permissions.CertificateWrite)]
    public async Task<Certificate> ReplaceBySelfSigned(Guid projectId, Guid certificateId, CertificateSelfSignedParams? createParams = null)
    {
        var ret = await _certificateService.ReplaceBySelfSinged(projectId, certificateId, createParams);
        return ret;
    }

    [HttpPost("import")]
    [AuthorizeProjectPermission(Permissions.CertificateWrite)]
    public async Task<Certificate> CreateByImport(Guid projectId, CertificateImportParams importParams)
    {
        var ret = await _certificateService.CreateByImport(projectId, importParams);
        return ret;
    }

    [HttpPost("{certificateId:guid}/import")]
    [AuthorizeProjectPermission(Permissions.CertificateWrite)]
    public async Task<Certificate> ReplaceByImport(Guid projectId, Guid certificateId, CertificateImportParams importParams)
    {
        var ret = await _certificateService.ReplaceByImport(projectId, certificateId, importParams);
        return ret;
    }

    [HttpGet("{certificateId:guid}")]
    [AuthorizeProjectPermission(Permissions.CertificateRead)]
    public Task<CertificateData> Get(Guid projectId, Guid certificateId, bool includeSummary = false)
    {
        return _certificateService.Get(projectId, certificateId, includeSummary);
    }

    [HttpDelete("{certificateId:guid}")]
    [AuthorizeProjectPermission(Permissions.CertificateWrite)]
    public Task Delete(Guid projectId, Guid certificateId)
    {
        return _certificateService.Delete(projectId, certificateId);
    }

    [HttpGet]
    [AuthorizeProjectPermission(Permissions.CertificateRead)]
    public Task<IEnumerable<CertificateData>> List(Guid projectId, string? search = null, bool includeSummary = false,
        int recordIndex = 0, int recordCount = 300)
    {
        return _certificateService.List(projectId, search, includeSummary: includeSummary,
            recordIndex: recordIndex, recordCount: recordCount);
    }
}
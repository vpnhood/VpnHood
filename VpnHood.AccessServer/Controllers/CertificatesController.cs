using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GrayMint.Authorization.RoleManagement.RoleAuthorizations;
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
    [AuthorizePermission(Permissions.CertificateWrite)]
    public Task<Certificate> Create(Guid projectId, CertificateCreateParams? createParams)
    {
        return _certificateService.Create(projectId, createParams);
    }

    [HttpGet("{certificateId:guid}")]
    [AuthorizePermission(Permissions.CertificateRead)]
    public Task<CertificateData> Get(Guid projectId, Guid certificateId, bool includeSummary)
    {
        return _certificateService.Get(projectId, certificateId, includeSummary);
    }

    [HttpDelete("{certificateId:guid}")]
    [AuthorizePermission(Permissions.CertificateWrite)]
    public Task Delete(Guid projectId, Guid certificateId)
    {
        return _certificateService.Delete(projectId, certificateId);
    }

    [HttpPatch("{certificateId:guid}")]
    [AuthorizePermission(Permissions.CertificateWrite)]
    public Task<Certificate> Update(Guid projectId, Guid certificateId, CertificateUpdateParams updateParams)
    {
        return _certificateService.Update(projectId, certificateId, updateParams);
    }

    [HttpGet]
    [AuthorizePermission(Permissions.CertificateRead)]
    public Task<IEnumerable<CertificateData>> List(Guid projectId, string? search = null, bool includeSummary = false,
        int recordIndex = 0, int recordCount = 300)
    {
        return _certificateService.List(projectId, search, includeSummary: includeSummary, 
            recordIndex: recordIndex, recordCount: recordCount);
    }
}
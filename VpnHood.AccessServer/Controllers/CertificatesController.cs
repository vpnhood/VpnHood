using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Exceptions;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.MultiLevelAuthorization.Services;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Security;
using VpnHood.Server;

namespace VpnHood.AccessServer.Controllers;

[Route("/api/v{version:apiVersion}/projects/{projectId:guid}/certificates")]
public class CertificatesController : SuperController<CertificatesController>
{
    public CertificatesController(ILogger<CertificatesController> logger, VhContext vhContext, MultilevelAuthService multilevelAuthService)
        : base(logger, vhContext, multilevelAuthService)
    {
    }

    [HttpPost]
    public async Task<Certificate> Create(Guid projectId, CertificateCreateParams? createParams)
    {
        await VerifyUserPermission( projectId, Permissions.CertificateWrite);

        // check user quota
        using var singleRequest = SingleRequest.Start($"CreateCertificate_{CurrentUserId}");
        if (VhContext.Certificates.Count(x => x.ProjectId == projectId) >= QuotaConstants.CertificateCount)
            throw new QuotaException(nameof(VhContext.Certificates), QuotaConstants.CertificateCount);

        var certificateModel = CreateInternal(projectId, createParams);
        VhContext.Certificates.Add(certificateModel);
        await VhContext.SaveChangesAsync();
        
        return certificateModel.ToDto(true);
    }

    internal static CertificateModel CreateInternal(Guid projectId, CertificateCreateParams? createParams)
    {
        createParams ??= new CertificateCreateParams();
        if (!string.IsNullOrEmpty(createParams.SubjectName) && createParams.RawData?.Length > 0)
            throw new InvalidOperationException(
                $"Could not set both {createParams.SubjectName} and {createParams.RawData} together!");

        // create cert
        var certificateRawBuffer = createParams.RawData?.Length > 0
            ? createParams.RawData
            : CertificateUtil.CreateSelfSigned(createParams.SubjectName).Export(X509ContentType.Pfx);

        // add cert into 
        var x509Certificate2 = new X509Certificate2(certificateRawBuffer, createParams.Password,
            X509KeyStorageFlags.Exportable);
        certificateRawBuffer = x509Certificate2.Export(X509ContentType.Pfx); //removing password

        var certificateModel = new CertificateModel()
        {
            CertificateId = Guid.NewGuid(),
            ProjectId = projectId,
            RawData = certificateRawBuffer,
            CommonName = x509Certificate2.GetNameInfo(X509NameType.DnsName, false),
            ExpirationTime = x509Certificate2.NotAfter.ToUniversalTime(),
            CreatedTime = DateTime.UtcNow
        };

        return certificateModel;
    }


    [HttpGet("{certificateId:guid}")]
    public async Task<Certificate> Get(Guid projectId, Guid certificateId)
    {
        await VerifyUserPermission( projectId, Permissions.CertificateRead);

        var certificateModel = await VhContext.Certificates.SingleAsync(x => x.ProjectId == projectId && x.CertificateId == certificateId);
        return certificateModel.ToDto();
    }

    [HttpDelete("{certificateId:guid}")]
    public async Task Delete(Guid projectId, Guid certificateId)
    {
        await VerifyUserPermission( projectId, Permissions.CertificateWrite);

        var certificate = await VhContext.Certificates
            .SingleAsync(x => x.ProjectId == projectId && x.CertificateId == certificateId);
        VhContext.Certificates.Remove(certificate);
        await VhContext.SaveChangesAsync();
    }

    [HttpPatch("{certificateId:guid}")]
    public async Task<Certificate> Update(Guid projectId, Guid certificateId, CertificateUpdateParams updateParams)
    {
        await VerifyUserPermission( projectId, Permissions.CertificateWrite);

        var certificateModel = await VhContext.Certificates
            .SingleAsync(x => x.ProjectId == projectId && x.CertificateId == certificateId);

        if (updateParams.RawData != null)
        {
            X509Certificate2 x509Certificate2 = new(updateParams.RawData, updateParams.Password?.Value, X509KeyStorageFlags.Exportable);
            certificateModel.CommonName = x509Certificate2.GetNameInfo(X509NameType.DnsName, false);
            certificateModel.RawData = x509Certificate2.Export(X509ContentType.Pfx);
        }

        await VhContext.SaveChangesAsync();
        return certificateModel.ToDto();
    }

    [HttpGet]
    public async Task<Certificate[]> List(Guid projectId, int recordIndex = 0, int recordCount = 300)
    {
        await VerifyUserPermission( projectId, Permissions.CertificateRead);

        var query = VhContext.Certificates.Where(x => x.ProjectId == projectId);
        var res = await query
            .Skip(recordIndex)
            .Take(recordCount)
            .Select(x => x.ToDto(false))
            .ToArrayAsync();

        return res;
    }
}
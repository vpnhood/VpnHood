using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using GrayMint.Common.Utils;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Exceptions;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Persistence;
using VpnHood.Server;

namespace VpnHood.AccessServer.Services;

public class CertificateService : ControllerBase
{
    private readonly VhContext _vhContext;
    public CertificateService(VhContext vhContext)
    {
        _vhContext = vhContext;
    }

    public async Task<Certificate> Create(Guid projectId, CertificateCreateParams? createParams)
    {
        // check user quota
        using var singleRequest = await AsyncLock.LockAsync($"{projectId}_CreateCertificate");
        if (_vhContext.Certificates.Count(x => x.ProjectId == projectId) >= QuotaConstants.CertificateCount)
            throw new QuotaException(nameof(_vhContext.Certificates), QuotaConstants.CertificateCount);

        var certificateModel = CreateInternal(projectId, createParams);
        _vhContext.Certificates.Add(certificateModel);
        await _vhContext.SaveChangesAsync();

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


    public async Task<CertificateData> Get(Guid projectId, Guid certificateId, bool includeSummary = false)
    {
        var list = await List(projectId, certificateId, includeSummary: includeSummary);
        return list.Single();
    }

    public async Task Delete(Guid projectId, Guid certificateId)
    {
        var certificate = await _vhContext.Certificates
            .SingleAsync(x => x.ProjectId == projectId && x.CertificateId == certificateId);
        _vhContext.Certificates.Remove(certificate);
        await _vhContext.SaveChangesAsync();
    }

    public async Task<Certificate> Update(Guid projectId, Guid certificateId, CertificateUpdateParams updateParams)
    {
        var certificateModel = await _vhContext.Certificates
            .SingleAsync(x => x.ProjectId == projectId && x.CertificateId == certificateId);

        if (updateParams.RawData != null)
        {
            X509Certificate2 x509Certificate2 = new(updateParams.RawData, updateParams.Password?.Value, X509KeyStorageFlags.Exportable);
            certificateModel.CommonName = x509Certificate2.GetNameInfo(X509NameType.DnsName, false);
            certificateModel.RawData = x509Certificate2.Export(X509ContentType.Pfx);
        }

        await _vhContext.SaveChangesAsync();
        return certificateModel.ToDto();
    }

    public async Task<IEnumerable<CertificateData>> List(Guid projectId, Guid? certificateId = null,
        bool includeSummary = false, int recordIndex = 0, int recordCount = 300)
    {
        var query = _vhContext.Certificates
            .Include(x => x.ServerFarms)
            .Where(x => x.ProjectId == projectId)
            .Where(x => certificateId == null || x.CertificateId == certificateId);

        var res = await query
            .OrderBy(x => x.CommonName)
            .Skip(recordIndex)
            .Take(recordCount)
            .Select(x => new CertificateData
            {
                Certificate = new Certificate
                {
                    CertificateId = x.CertificateId,
                    CommonName = x.CommonName,
                    CreatedTime = x.CreatedTime,
                    ExpirationTime = x.ExpirationTime
                },
                Summary = includeSummary ? new CertificateSummary
                {
                    ServerFarmCount = x.ServerFarms!.Count()
                } : null
            })
            .ToArrayAsync();

        return res;
    }
}
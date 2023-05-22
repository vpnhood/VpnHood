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
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Persistence;
using VpnHood.Server;

namespace VpnHood.AccessServer.Services;

public class CertificateService : ControllerBase
{
    private readonly VhContext _vhContext;
    private readonly SubscriptionService _subscriptionService;

    public CertificateService(VhContext vhContext, SubscriptionService subscriptionService)
    {
        _vhContext = vhContext;
        _subscriptionService = subscriptionService;
    }

    public async Task<Certificate> CreateSelfSinged(Guid projectId, Guid? certificateId, CertificateSelfSignedParams? createParams)
    {
        // check user quota
        using var singleRequest = await AsyncLock.LockAsync($"{projectId}_CreateCertificate");
        if (certificateId != null)
            await _subscriptionService.AuthorizeAddCertificate(projectId);

        createParams ??= new CertificateSelfSignedParams();
        var x509Certificate2 = CertificateUtil.CreateSelfSigned(createParams.SubjectName);
        var certificateModel = AddOrUpdate(projectId, certificateId, x509Certificate2);
        return certificateModel.ToDto(true);
    }

    public async Task<Certificate> ImportSelfSinged(Guid projectId, Guid? certificateId, CertificateImportParams importParams)
    {
        // check user quota
        using var singleRequest = await AsyncLock.LockAsync($"{projectId}_CreateCertificate");
        if (certificateId != null)
            await _subscriptionService.AuthorizeAddCertificate(projectId);

        var x509Certificate2 = new X509Certificate2(importParams.RawData, importParams.Password, X509KeyStorageFlags.Exportable);
        var certificateModel = AddOrUpdate(projectId, certificateId, x509Certificate2);
        return certificateModel.ToDto(true);
    }

    private static CertificateModel AddOrUpdate(Guid projectId, Guid? certificateId, X509Certificate2 x509Certificate2)
    {
        // create cert
        var certificateRawBuffer = x509Certificate2.RawData?.Length > 0
            ? createParams.RawData
            : CertificateUtil.CreateSelfSigned(createParams.CommonName).Export(X509ContentType.Pfx);

        // add cert into 
        var x509Certificate2 = new X509Certificate2(certificateRawBuffer, createParams.Password,
            X509KeyStorageFlags.Exportable);
        certificateRawBuffer = x509Certificate2.Export(X509ContentType.Pfx); //removing password

        var certificateModel = new CertificateModel()
        {
            CertificateId = Guid.NewGuid(),
            ProjectId = projectId,
            RawData = certificateRawBuffer,
            Thumbprint = x509Certificate2.Thumbprint,
            CommonName = x509Certificate2.GetNameInfo(X509NameType.DnsName, false),
            ExpirationTime = x509Certificate2.NotAfter.ToUniversalTime(),
            CreatedTime = DateTime.UtcNow
        };

        return certificateModel;
    }


    internal static CertificateModel CreateInternal(Guid projectId, CertificateSelfSignedParams? createParams)
    {
        createParams ??= new CertificateSelfSignedParams();
        if (!string.IsNullOrEmpty(createParams.SubjectName) && createParams.RawData?.Length > 0)
            throw new InvalidOperationException(
                $"Could not set both {createParams.SubjectName} and {createParams.RawData} together!");



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
        var list = await List(projectId, certificateId: certificateId, includeSummary: includeSummary);
        return list.Single();
    }

    public async Task Delete(Guid projectId, Guid certificateId)
    {
        var serverFarmCount = await _vhContext.ServerFarms
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .Where(x => x.CertificateId == certificateId)
            .CountAsync();

        if (serverFarmCount > 0)
            throw new InvalidOperationException($"The certificate is in use by {serverFarmCount} server farms.");

        var certificate = await _vhContext.Certificates
            .Where(x => x.ProjectId == projectId && x.CertificateId == certificateId && !x.IsDeleted)
            .SingleAsync();

        certificate.IsDeleted = true;
        await _vhContext.SaveChangesAsync();
    }

    public async Task<Certificate> Update(Guid projectId, Guid certificateId, CertificateUpdateParams updateParams)
    {
        var certificateModel = await _vhContext.Certificates
            .SingleAsync(x => x.ProjectId == projectId && x.CertificateId == certificateId && !x.IsDeleted);

        if (updateParams.RawData != null)
        {
            X509Certificate2 x509Certificate2 = new(updateParams.RawData, updateParams.Password?.Value, X509KeyStorageFlags.Exportable);
            certificateModel.CommonName = x509Certificate2.GetNameInfo(X509NameType.DnsName, false);
            certificateModel.RawData = x509Certificate2.Export(X509ContentType.Pfx);
        }

        await _vhContext.SaveChangesAsync();
        return certificateModel.ToDto();
    }

    public async Task<IEnumerable<CertificateData>> List(Guid projectId, string? search = null,
        Guid? certificateId = null,
        bool includeSummary = false, int recordIndex = 0, int recordCount = 300)
    {
        var query = _vhContext.Certificates
            .Include(x => x.ServerFarms!.Where(y => !y.IsDeleted).OrderBy(y => y.ServerFarmName).Take(5))
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .Where(x => certificateId == null || x.CertificateId == certificateId)
            .Where(x =>
                string.IsNullOrEmpty(search) ||
                x.CommonName.Contains(search) ||
                x.CertificateId.ToString() == search);

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
                    ExpirationTime = x.ExpirationTime,
                    RawData = null,
                },
                ServerFarms = includeSummary
                    ? x.ServerFarms!.Select(y => IdName.Create(y.ServerFarmId, y.ServerFarmName))
                    : null
            })
            .ToArrayAsync();

        return res;
    }
}
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
using VpnHood.Server.Access;

namespace VpnHood.AccessServer.Services;

public class CertificateService
{
    private readonly VhContext _vhContext;
    private readonly SubscriptionService _subscriptionService;

    public CertificateService(VhContext vhContext, SubscriptionService subscriptionService)
    {
        _vhContext = vhContext;
        _subscriptionService = subscriptionService;
    }

    internal async Task<CertificateModel> CreateSelfSingedInternal(Guid projectId)
    {
        var x509Certificate2 = CertificateUtil.CreateSelfSigned(subjectName: null);
        return await Add(projectId, x509Certificate2);
    }

    public async Task<Certificate> CreateSelfSinged(Guid projectId, CertificateSelfSignedParams? createParams)
    {
        // check user quota
        using var singleRequest = await AsyncLock.LockAsync($"{projectId}_CreateCertificate");
        await _subscriptionService.AuthorizeAddCertificate(projectId);

        createParams ??= new CertificateSelfSignedParams();
        var x509Certificate2 = CertificateUtil.CreateSelfSigned(createParams.SubjectName, createParams.ExpirationTime);
        var certificateModel = await Add(projectId, x509Certificate2);
        await _vhContext.SaveChangesAsync();
        return certificateModel.ToDto(true);
    }

    public async Task<Certificate> ReplaceBySelfSinged(Guid projectId, Guid certificateId, CertificateSelfSignedParams? createParams)
    {
        createParams ??= new CertificateSelfSignedParams();
        var x509Certificate2 = CertificateUtil.CreateSelfSigned(createParams.SubjectName, createParams.ExpirationTime);
        var certificateModel = await Update(projectId, certificateId, x509Certificate2);
        await _vhContext.SaveChangesAsync();
        return certificateModel.ToDto(true);
    }

    public async Task<Certificate> CreateByImport(Guid projectId, CertificateImportParams importParams)
    {
        // check user quota
        using var singleRequest = await AsyncLock.LockAsync($"{projectId}_CreateCertificate");
        await _subscriptionService.AuthorizeAddCertificate(projectId);

        var x509Certificate2 = new X509Certificate2(importParams.RawData, importParams.Password, X509KeyStorageFlags.Exportable);
        var certificateModel = await Add(projectId, x509Certificate2);
        await _vhContext.SaveChangesAsync();
        return certificateModel.ToDto(true);
    }

    public async Task<Certificate> ReplaceByImport(Guid projectId, Guid certificateId, CertificateImportParams importParams)
    {
        var x509Certificate2 = new X509Certificate2(importParams.RawData, importParams.Password, X509KeyStorageFlags.Exportable);
        var certificateModel = await Update(projectId, certificateId, x509Certificate2);
        await _vhContext.SaveChangesAsync();
        return certificateModel.ToDto(true);
    }

    private async Task<CertificateModel> Update(Guid projectId, Guid certificateId, X509Certificate2 x509Certificate2)
    {
        var certificate = await _vhContext
            .Certificates
            .SingleAsync(x => x.ProjectId == projectId && x.CertificateId == certificateId);

        // read old common name
        var oldX509Certificate = new X509Certificate2(certificate.RawData);
        var oldCommonName = oldX509Certificate.GetNameInfo(X509NameType.DnsName, false);
        var commonName = x509Certificate2.GetNameInfo(X509NameType.DnsName, false);
        if (commonName != oldCommonName)
            throw new InvalidOperationException(
                $"The Common Name (CN) must match the existing value in order to perform an update. OldName: {oldCommonName}, NewName:{commonName}");

        certificate.RawData = x509Certificate2.Export(X509ContentType.Pfx);
        certificate.Thumbprint = x509Certificate2.Thumbprint;
        certificate.CommonName = x509Certificate2.GetNameInfo(X509NameType.DnsName, false);
        certificate.IssueTime = x509Certificate2.NotBefore.ToUniversalTime();
        certificate.ExpirationTime = x509Certificate2.NotAfter.ToUniversalTime();
        certificate.IsVerified = x509Certificate2.Verify();
        certificate.CreatedTime = DateTime.UtcNow;
        return certificate;
    }

    private async Task<CertificateModel> Add(Guid projectId, X509Certificate2 x509Certificate2)
    {
        var certificate = new CertificateModel
        {
            CertificateId = Guid.NewGuid(),
            ProjectId = projectId,
            CreatedTime = DateTime.UtcNow,
            RawData = x509Certificate2.Export(X509ContentType.Pfx),
            Thumbprint = x509Certificate2.Thumbprint,
            CommonName = x509Certificate2.GetNameInfo(X509NameType.DnsName, false),
            IssueTime = x509Certificate2.NotBefore.ToUniversalTime(),
            ExpirationTime = x509Certificate2.NotAfter.ToUniversalTime(),
            IsVerified = x509Certificate2.Verify()
        };

        if (certificate.CommonName.Contains('*'))
            throw new NotSupportedException("Wildcard certificates are currently not supported.");

        await _vhContext.Certificates.AddAsync(certificate);

        return certificate;
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
                    IssueTime = x.IssueTime,
                    IsVerified = x.IsVerified,
                    Thumbprint = x.Thumbprint,
                    RawData = null,
                },
                ServerFarms = includeSummary
                    ? x.ServerFarms!
                        .Where(y=>!y.IsDeleted)
                        .Select(y => IdName.Create(y.ServerFarmId, y.ServerFarmName))
                    : null
            })
            .ToArrayAsync();

        return res;
    }
}
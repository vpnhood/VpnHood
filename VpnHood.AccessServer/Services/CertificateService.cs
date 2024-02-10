using System.Security.Cryptography.X509Certificates;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos.Certificate;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.Server.Access;

namespace VpnHood.AccessServer.Services;

public class CertificateService(
    VhRepo vhRepo,
    CertificateSignerService signerService)
{
    internal Task<CertificateModel> CreateSelfSingedInternal(Guid projectId)
    {
        var x509Certificate2 = CertificateUtil.CreateSelfSigned(subjectName: null);
        return Add(projectId, x509Certificate2);
    }

    public async Task<Certificate> CreateSelfSinged(Guid projectId, CertificateSelfSignedParams? createParams)
    {
        // check user quota
        createParams ??= new CertificateSelfSignedParams()
        {
            CertificateSigningRequest = new CertificateSigningRequest { CommonName = CertificateUtil.CreateRandomDns() }
        };

        var x509Certificate2 = CertificateUtil.CreateSelfSigned(createParams.CertificateSigningRequest.BuildSubjectName(), createParams.ExpirationTime);
        var certificateModel = await Add(projectId, x509Certificate2);
        await vhRepo.SaveChangesAsync();
        return certificateModel.ToDto(true);
    }

    public async Task<Certificate> CreateTrusted(Guid projectId, CertificateSigningRequest csr)
    {
        if (string.IsNullOrWhiteSpace(csr.CommonName))
            throw new ArgumentException("CommonName is required.", nameof(csr.CommonName));

        if (csr.CommonName.Contains('*'))
            throw new NotSupportedException("Wildcard certificates are not supported.");

        var orderId = signerService.NewOrder(csr);

        // create a self-signed certificate till we get the real one
        var x509Certificate2 = CertificateUtil.CreateSelfSigned(csr.BuildSubjectName(), DateTime.UtcNow.AddYears(10));
        var certificateModel = await Add(projectId, x509Certificate2);
        var project = await vhRepo.ProjectGet(projectId);
        project.CsrCount++;
        await vhRepo.SaveChangesAsync();
        
        return certificateModel.ToDto();
    }

    public async Task<Certificate> CreateByImport(Guid projectId, CertificateImportParams importParams)
    {
        var x509Certificate2 = new X509Certificate2(importParams.RawData, importParams.Password, X509KeyStorageFlags.Exportable);
        var certificateModel = await Add(projectId, x509Certificate2);
        await vhRepo.SaveChangesAsync();
        return certificateModel.ToDto(true);
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
            DnsVerificationText = null,
            SubjectName = x509Certificate2.Subject,
            IsVerified = x509Certificate2.Verify()
        };

        if (certificate.CommonName.Contains('*'))
            throw new NotSupportedException("Wildcard certificates are not supported.");

        await vhRepo.AddAsync(certificate);
        return certificate;
    }

    public async Task<CertificateData> Get(Guid projectId, Guid certificateId, bool includeSummary = false)
    {
        var list = await List(projectId, certificateId: certificateId, includeSummary: includeSummary);
        return list.Single();
    }

    public async Task Delete(Guid projectId, Guid certificateId)
    {
        var serverFarmCount = await vhRepo.ServerFarmCount(projectId: projectId, certificateId: certificateId);
        if (serverFarmCount > 0)
            throw new InvalidOperationException($"The certificate is in use by {serverFarmCount} server farms.");

        await vhRepo.CertificateDelete(projectId, certificateId);
    }

    public async Task<IEnumerable<CertificateData>> List(Guid projectId, string? search = null,
        Guid? certificateId = null,
        bool includeSummary = false, int recordIndex = 0, int recordCount = 300)
    {
        var res = await vhRepo.CertificateList(projectId, search, certificateId, includeSummary, recordIndex, recordCount);
        var ret = res.Select(x => new CertificateData
        {
            Certificate = x.Certificate.ToDto(),
            ServerFarms = x.ServerFarms,
        });

        return ret;
    }
}
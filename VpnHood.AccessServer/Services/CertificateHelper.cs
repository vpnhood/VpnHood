using System.Security.Cryptography.X509Certificates;
using VpnHood.AccessServer.Dtos.Certificate;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.Server.Access;

namespace VpnHood.AccessServer.Services;

public static class CertificateHelper
{
    public static CertificateModel BuildByImport(Guid projectId, Guid serverFarmId, CertificateImportParams importParams)
    {
        var x509Certificate2 = new X509Certificate2(importParams.RawData, importParams.Password, X509KeyStorageFlags.Exportable);
        return BuildModel(projectId, serverFarmId, x509Certificate2);
    }

    public static CertificateModel BuildModel(Guid projectId, Guid serverFarmId, X509Certificate2 x509Certificate2)
    {
        var certificate = new CertificateModel
        {
            CertificateId = Guid.NewGuid(),
            ServerFarmId = serverFarmId,
            ProjectId = projectId,
            CreatedTime = DateTime.UtcNow,
            RawData = x509Certificate2.Export(X509ContentType.Pfx),
            Thumbprint = x509Certificate2.Thumbprint,
            CommonName = x509Certificate2.GetNameInfo(X509NameType.DnsName, false),
            IssueTime = x509Certificate2.NotBefore.ToUniversalTime(),
            ExpirationTime = x509Certificate2.NotAfter.ToUniversalTime(),
            SubjectName = x509Certificate2.Subject,
            IsTrusted = x509Certificate2.Verify(),
            IsDefault = true,
            IsDeleted = false,
            AutoRenew = false,
            RenewCount = 0,
            RenewError = null,
            RenewErrorCount = 0,
            RenewErrorTime = null,
            RenewToken = null,
            RenewKeyAuthorization = null,
            RenewInprogress = false,
        };

        if (certificate.CommonName.Contains('*'))
            throw new NotSupportedException("Wildcard certificates are not supported.");

        return certificate;
    }

    public static CertificateModel BuildSelfSinged(Guid projectId, Guid serverFarmId, CertificateCreateParams? createParams)
    {
        // check user quota
        createParams ??= new CertificateCreateParams
        {
            CertificateSigningRequest = new CertificateSigningRequest { CommonName = CertificateUtil.CreateRandomDns() }
        };

        var expirationTime = createParams.ExpirationTime ?? DateTime.UtcNow.AddYears(Random.Shared.Next(8, 12));
        var x509Certificate2 = CertificateUtil.CreateSelfSigned(subjectName: createParams.CertificateSigningRequest.BuildSubjectName(), expirationTime);
        return BuildModel(projectId, serverFarmId, x509Certificate2);
    }
}
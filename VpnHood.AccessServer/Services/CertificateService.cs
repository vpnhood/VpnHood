using System.Security.Cryptography.X509Certificates;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos.Certificates;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.AccessServer.Providers.Acme;
using VpnHood.AccessServer.Repos;
using VpnHood.Server.Access;

namespace VpnHood.AccessServer.Services;

public class CertificateService(
    VhRepo vhRepo,
    ServerConfigureService serverConfigureService)
{
    public async Task<Certificate> Replace(Guid projectId, Guid serverFarmId, CertificateCreateParams? createParams)
    {
        // make sure farm belong to this project
        var serverFarm = await vhRepo.ServerFarmGet(projectId, serverFarmId, includeCertificates: true);

        // build new certificate
        var certificate = BuildSelfSinged(serverFarm.ProjectId, serverFarmId: serverFarm.ServerFarmId,
            createParams: createParams);
        serverFarm.UseHostName = certificate.IsValidated; // disable hostname for self-signed certificate

        // keep the recent certificates by MaxCertificateCount and delete the rest
        await ManageCertificates(serverFarm, certificate);

        // add after removing the old certificates to let handle duplicate common name
        await vhRepo.AddAsync(certificate);

        // invalidate farm cache
        await serverConfigureService.SaveChangesAndInvalidateServerFarm(certificate.ProjectId, serverFarmId, true);
        return certificate.ToDto();
    }

    public async Task<Certificate> Import(Guid projectId, Guid serverFarmId, CertificateImportParams importParams)
    {
        // make sure farm belong to this project
        var serverFarm = await vhRepo.ServerFarmGet(projectId, serverFarmId, includeCertificates: true);

        // build new certificate
        var certificate = BuildByImport(serverFarm.ProjectId, serverFarm.ServerFarmId, importParams);
        serverFarm.UseHostName = certificate.IsValidated; // disable hostname for self-signed certificate

        // keep the recent certificates by MaxCertificateCount and delete the rest
        await ManageCertificates(serverFarm, certificate);

        // add after removing the old certificates to let handle duplicate common name
        await vhRepo.AddAsync(certificate);

        // invalidate farm cache
        await serverConfigureService.SaveChangesAndInvalidateServerFarm(certificate.ProjectId, 
            serverFarmId: serverFarmId, reconfigureServers: true);

        return certificate.ToDto();
    }

    // keep the recent certificates by MaxCertificateCount and delete the rest.
    // Archive the previous IsInToken certificate and remove it from token
    private static Task ManageCertificates(ServerFarmModel serverFarm, CertificateModel newCertificate)
    {
        var certificates = serverFarm.Certificates!
            .OrderByDescending(x => x.CreatedTime)
            .ToList();

        // reset archive certificates
        foreach (var cert in certificates) {
            cert.AutoValidate = false;
            cert.IsInToken = false;
        }

        // delete duplicate common name
        var duplicates = certificates.Where(x =>
            x.CommonName.Equals(newCertificate.CommonName, StringComparison.OrdinalIgnoreCase));
        foreach (var cert in duplicates.ToArray()) {
            cert.IsDeleted = true;
            certificates.Remove(cert);
        }

        // delete the rest; -1 for the new certificate
        foreach (var cert in certificates.Skip(serverFarm.MaxCertificateCount - 1))
            cert.IsDeleted = true;

        return Task.CompletedTask;
    }

    public async Task<Certificate[]> List(Guid projectId, Guid serverFarmId)
    {
        // make sure farm belong to this project
        var serverFarm = await vhRepo.ServerFarmGet(projectId, serverFarmId, includeCertificates: true);
        var certificates = serverFarm.Certificates!
            .OrderByDescending(x => x.CreatedTime)
            .Select(x => x.ToDto())
            .ToArray();

        return certificates;
    }


    public async Task Delete(Guid projectId, Guid certificateId)
    {
        var certificate = await vhRepo.CertificateGet(projectId, certificateId);
        if (certificate.IsInToken)
            throw new InvalidOperationException("Default certificate can not be deleted.");

        await vhRepo.CertificateDelete(projectId, certificateId);
        await vhRepo.SaveChangesAsync();
    }

    public Task<Certificate> Get(Guid projectId, Guid certificateId)
    {
        return vhRepo.CertificateGet(projectId, certificateId)
            .ContinueWith(x => x.Result.ToDto());
    }

    public static CertificateModel BuildByImport(Guid projectId, Guid serverFarmId,
        CertificateImportParams importParams)
    {
        var x509Certificate2 =
            new X509Certificate2(importParams.RawData, importParams.Password, X509KeyStorageFlags.Exportable);
        return BuildModel(projectId, serverFarmId, x509Certificate2);
    }

    private static CertificateModel BuildSelfSinged(Guid projectId, Guid serverFarmId,
        CertificateCreateParams? createParams)
    {
        // check user quota
        createParams ??= new CertificateCreateParams {
            CertificateSigningRequest = new CertificateSigningRequest { CommonName = CertificateUtil.CreateRandomDns() }
        };

        var expirationTime = createParams.ExpirationTime ?? DateTime.UtcNow.AddYears(Random.Shared.Next(8, 12));
        var x509Certificate2 =
            CertificateUtil.CreateSelfSigned(subjectName: createParams.CertificateSigningRequest.BuildSubjectName(),
                expirationTime);
        return BuildModel(projectId, serverFarmId, x509Certificate2);
    }

    private static CertificateModel BuildModel(Guid projectId, Guid serverFarmId, X509Certificate2 x509Certificate2)
    {
        var certificate = new CertificateModel {
            CertificateId = Guid.NewGuid(),
            ServerFarmId = serverFarmId,
            ProjectId = projectId,
            CreatedTime = DateTime.UtcNow,
            RawData = x509Certificate2.Export(X509ContentType.Pfx),
            Thumbprint = x509Certificate2.Thumbprint,
            CommonName = x509Certificate2.GetNameInfo(X509NameType.DnsName, false),
            IssueTime = x509Certificate2.NotBefore.ToUniversalTime(),
            ExpirationTime = x509Certificate2.NotAfter.ToUniversalTime(),
            IsValidated = x509Certificate2.Verify(),
            IsInToken = true,
            IsDeleted = false,
            AutoValidate = false,
            ValidateCount = 0,
            ValidateError = null,
            ValidateErrorCount = 0,
            ValidateErrorTime = null,
            ValidateInprogress = false,
            ValidateKeyAuthorization = null,
            ValidateToken = null
        };

        if (certificate.CommonName.Contains('*'))
            throw new NotSupportedException("Wildcard certificates are not supported.");

        return certificate;
    }
}
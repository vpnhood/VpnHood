using System.Security.Cryptography.X509Certificates;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos.Certificates;
using VpnHood.AccessServer.Persistence.Models;
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
        serverFarm.UseHostName = false; // disable hostname for self-signed certificate
        
        // remove all farm certificates
        foreach (var cert in serverFarm.Certificates!)
            await vhRepo.CertificateDelete(projectId, cert.CertificateId);

        var certificate = BuildSelfSinged(serverFarm.ProjectId, serverFarmId: serverFarm.ServerFarmId, createParams: createParams);
        await vhRepo.AddAsync(certificate);
        await vhRepo.SaveChangesAsync();

        // invalidate farm cache
        await serverConfigureService.InvalidateServerFarm(certificate.ProjectId, serverFarmId, true);

        return certificate.ToDto();
    }

    public async Task<Certificate> Import(Guid projectId, Guid serverFarmId, CertificateImportParams importParams)
    {
        // make sure farm belong to this project
        var serverFarm = await vhRepo.ServerFarmGet(projectId, serverFarmId, includeCertificates: true);

        // remove all farm certificates
        foreach (var cert in serverFarm.Certificates!)
            await vhRepo.CertificateDelete(projectId, cert.CertificateId);

        // replace with the new one
        var certificate = BuildByImport(serverFarm.ProjectId, serverFarm.ServerFarmId, importParams);
        await vhRepo.AddAsync(certificate);
        await vhRepo.SaveChangesAsync();

        // invalidate farm cache
        await serverConfigureService.InvalidateServerFarm(certificate.ProjectId, serverFarmId, true);

        return certificate.ToDto();
    }

    public async Task Delete(Guid projectId, Guid certificateId)
    {
        var certificate = await vhRepo.CertificateGet(projectId, certificateId);
        if (certificate.IsDefault)
            throw new InvalidOperationException("Default certificate can not be deleted");

        await vhRepo.CertificateDelete(projectId, certificateId);
        await vhRepo.SaveChangesAsync();
    }

    public Task<Certificate> Get(Guid projectId, Guid certificateId)
    {
        return vhRepo.CertificateGet(projectId, certificateId)
            .ContinueWith(x => x.Result.ToDto());
    }

    public static CertificateModel BuildByImport(Guid projectId, Guid serverFarmId, CertificateImportParams importParams)
    {
        var x509Certificate2 = new X509Certificate2(importParams.RawData, importParams.Password, X509KeyStorageFlags.Exportable);
        return BuildModel(projectId, serverFarmId, x509Certificate2);
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
            IsValidated = x509Certificate2.Verify(),
            IsDefault = true,
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
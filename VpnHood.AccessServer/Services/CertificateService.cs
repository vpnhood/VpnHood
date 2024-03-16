using System.Security.Cryptography.X509Certificates;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos.Certificates;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.AccessServer.Services.Acme;
using VpnHood.Server.Access;

namespace VpnHood.AccessServer.Services;

public class CertificateService(
    VhRepo vhRepo,
    ILogger<CertificateService> logger,
    ServerConfigureService serverConfigureService,
    IAcmeOrderFactory acmeOrderFactory)
{
    public async Task<Certificate> Replace(Guid projectId, Guid serverFarmId, CertificateCreateParams? createParams)
    {
        // make sure farm belong to this project
        var serverFarm = await vhRepo.ServerFarmGet(projectId, serverFarmId, includeCertificates: true);

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

    public async Task Renew(Guid projectId, Guid serverFarmId, CancellationToken cancellationToken)
    {
        var serverFarm = await vhRepo.ServerFarmGet(projectId, serverFarmId,
            includeCertificates: true, includeProject: true);
        await Renew(serverFarm.Certificate!, cancellationToken);
    }

    private async Task Renew(CertificateModel certificate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(certificate.Project);
        ArgumentNullException.ThrowIfNull(certificate.ServerFarm);
        var project = certificate.Project;
        if (certificate.RenewInprogress)
            return;

        try
        {
            // create Csr from certificate
            logger.LogInformation("Renewing certificate. ProjectId: {ProjectId}, CommonName: {CommonName}", project.ProjectId, certificate.CommonName);
            certificate.RenewInprogress = true;

            // create account if not exists
            if (project.LetsEncryptAccount?.AccountPem == null)
            {
                logger.LogInformation("Creating acme account. ProjectId: {ProjectId}", project.ProjectId);
                var accountPem = await acmeOrderFactory.CreateNewAccount($"project-{project.ProjectId}@vpnhood.com");
                project.LetsEncryptAccount = new LetsEncryptAccount
                {
                    AccountPem = accountPem
                };
                await vhRepo.SaveChangesAsync();
            }

            // create order
            var x509Certificate = new X509Certificate2(certificate.RawData);
            var csr = CreateCsrFromCertificate(x509Certificate);
            var acmeOrderService = await acmeOrderFactory.CreateOrder(project.LetsEncryptAccount.AccountPem, csr);
            certificate.RenewToken = acmeOrderService.Token;
            certificate.RenewKeyAuthorization = acmeOrderService.KeyAuthorization;
            await vhRepo.SaveChangesAsync();

            // wait for farm configuration
            await serverConfigureService.InvalidateServerFarm(certificate.ProjectId, certificate.ServerFarmId, true);
            await serverConfigureService.WaitForFarmConfiguration(certificate.ProjectId, certificate.ServerFarmId, cancellationToken);

            // validate order by manager
            logger.LogInformation("Validating certificate by the access server. ProjectId: {ProjectId}, CommonName: {CommonName}", project.ProjectId, certificate.CommonName);
            await ValidateByAccessServer(certificate.CommonName, certificate.RenewToken, certificate.RenewKeyAuthorization);
            logger.LogInformation("zzzzzzzzzz");

            // Validate
            while (true)
            {
                logger.LogInformation("Validating certificate by the provider. ProjectId: {ProjectId}, CommonName: {CommonName}", project.ProjectId, certificate.CommonName);
                var res = await acmeOrderService.Validate();
                if (res != null)
                {
                    certificate.RenewError = null;
                    certificate.RenewErrorTime = null;
                    certificate.RenewErrorCount = 0;
                    certificate.IsTrusted = true;
                    certificate.RenewCount++;
                    certificate.RawData = res.Export(X509ContentType.Pfx);
                    certificate.Thumbprint = res.Thumbprint;
                    certificate.ExpirationTime = res.NotAfter;
                    certificate.IssueTime = res.NotBefore;

                    await serverConfigureService.InvalidateServerFarm(certificate.ProjectId, certificate.ServerFarm.ServerFarmId, true);
                    break;
                }

                logger.LogInformation("Acme validate in pending. CommonName: {CommonName}", certificate.CommonName);
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
        catch (Exception ex)
        {
            certificate.RenewError = ex.Message;
            certificate.RenewErrorTime = DateTime.UtcNow;
            certificate.RenewErrorCount++;
        }
        finally
        {
            certificate.RenewToken = null;
            certificate.RenewKeyAuthorization = null;
            certificate.RenewInprogress = false;
            await vhRepo.SaveChangesAsync();
        }
    }

    private static async Task ValidateByAccessServer(string commonName, string token, string keyAuthorization)
    {
        var httpClient = new HttpClient();
        var url = $"http://{commonName}/.well-known/acme-challenge/{token}";
        var result = await httpClient.GetStringAsync(url);
        if (result != keyAuthorization)
            throw new Exception($"{commonName} or the farm servers have not been configured properly.");
    }
    private static CertificateSigningRequest CreateCsrFromCertificate(X509Certificate certificate)
    {
        var subject = certificate.Subject;

        // Split the subject into its components
        var components = subject.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim())
            .Select(part => part.Split(['='], 2))
            .Where(part => part.Length == 2)
            .ToDictionary(part => part[0], part => part[1], StringComparer.OrdinalIgnoreCase);

        // Extract and assign the properties
        components.TryGetValue("CN", out var cn);
        components.TryGetValue("O", out var o);
        components.TryGetValue("OU", out var ou);
        components.TryGetValue("C", out var c);
        components.TryGetValue("ST", out var st);
        components.TryGetValue("L", out var l);

        var csr = new CertificateSigningRequest
        {
            CommonName = cn,
            Organization = o,
            OrganizationUnit = ou,
            LocationCountry = c,
            LocationState = st,
            LocationCity = l
        };

        return csr;
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
            SubjectName = x509Certificate2.Subject,
            IsTrusted = x509Certificate2.Verify(),
            IsDefault = true,
            IsDeleted = false,
            AutoValidate = false,
            RenewCount = 0,
            RenewError = null,
            RenewErrorCount = 0,
            RenewErrorTime = null,
            RenewInprogress = false,
            RenewKeyAuthorization = null,
            RenewToken = null
        };

        if (certificate.CommonName.Contains('*'))
            throw new NotSupportedException("Wildcard certificates are not supported.");

        return certificate;
    }
}
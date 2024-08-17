using System.Security.Cryptography.X509Certificates;
using GrayMint.Common.AspNetCore.Jobs;
using GrayMint.Common.Utils;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Options;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.AccessServer.Providers.Acme;
using VpnHood.AccessServer.Repos;

namespace VpnHood.AccessServer.Services;

public class CertificateValidatorService(
    VhRepo vhRepo,
    ServerConfigureService serverConfigureService,
    IOptions<CertificateValidatorOptions> certificateValidatorOptions,
    IServiceScopeFactory serviceScopeFactory,
    ILogger<CertificateService> logger,
    IAcmeOrderFactory acmeOrderFactory)
    : IGrayMintJob
{
    public Task ValidateJob(Guid projectId, Guid serverFarmId, bool force, CancellationToken cancellationToken)
    {
        return ValidateJob(serviceScopeFactory, projectId, serverFarmId, force, cancellationToken);
    }

    private static async Task ValidateJob(IServiceScopeFactory serviceScopeFactory, Guid projectId, Guid serverFarmId,
        bool force, CancellationToken cancellationToken)
    {
        using var lockObject = await AsyncLock.LockAsync($"CertificateValidatorService_{projectId}_{serverFarmId}", TimeSpan.Zero, cancellationToken);
        if (!lockObject.Succeeded)
            return;

        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var certificateService = scope.ServiceProvider.GetRequiredService<CertificateValidatorService>();
        await certificateService.Validate(projectId, serverFarmId, force, cancellationToken);
    }


    private async Task Validate(Guid projectId, Guid serverFarmId, bool force, CancellationToken cancellationToken)
    {
        var serverFarm = await vhRepo.ServerFarmGet(projectId, serverFarmId, includeCertificates: true,
            includeProject: true, includeLetsEncryptAccount: true);
        await Validate(serverFarm.GetCertificateInToken(), force, cancellationToken);
    }

    private async Task Validate(CertificateModel certificate, bool force, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(certificate.Project);
        var project = certificate.Project;
        if (!force && certificate.ValidateInprogress)
            return;

        try {
            // create Csr from certificate
            logger.LogInformation("Renewing certificate. ProjectId: {ProjectId}, CertificateId: {CertificateId}, CommonName: {CommonName}",
                certificate.ProjectId, certificate.CertificateId, certificate.CommonName);
            certificate.ValidateInprogress = true;

            // create account if not exists
            if (project.LetsEncryptAccount?.AccountPem == null) {
                logger.LogInformation("Creating acme account. ProjectId: {ProjectId}", project.ProjectId);
                var accountPem = await acmeOrderFactory.CreateNewAccount($"project-{project.ProjectId}@vpnhood.com");
                project.LetsEncryptAccount = new LetsEncryptAccount {
                    AccountPem = accountPem
                };
                await vhRepo.SaveChangesAsync();
            }

            // create order
            var x509Certificate = new X509Certificate2(certificate.RawData);
            var csr = CreateCsrFromCertificate(x509Certificate);
            var acmeOrderService = await acmeOrderFactory.CreateOrder(project.LetsEncryptAccount.AccountPem, csr);
            certificate.ValidateToken = acmeOrderService.Token;
            certificate.ValidateKeyAuthorization = acmeOrderService.KeyAuthorization;

            // wait for farm configuration
            await serverConfigureService.SaveChangesAndInvalidateServerFarm(certificate.ProjectId,
                certificate.ServerFarmId, true);
            await serverConfigureService.WaitForFarmConfiguration(certificate.ProjectId, certificate.ServerFarmId,
                cancellationToken);

            // validate order by manager
            logger.LogInformation(
                "Validating certificate by the access server. ProjectId: {ProjectId}, CertificateId: {CertificateId}, CommonName: {CommonName}",
                certificate.ProjectId, certificate.CertificateId, certificate.CommonName);
            await ValidateByAccessServer(certificate.CommonName, certificate.ValidateToken,
                certificate.ValidateKeyAuthorization);

            // Validate
            while (true) {
                logger.LogInformation(
                    "Validating certificate by the provider. ProjectId: {ProjectId}, CertificateId: {CertificateId}, CommonName: {CommonName}",
                    certificate.ProjectId, certificate.CertificateId, certificate.CommonName);

                var res = await acmeOrderService.Validate();
                if (res != null) {
                    certificate.ValidateError = null;
                    certificate.ValidateErrorTime = null;
                    certificate.ValidateErrorCount = 0;
                    certificate.ValidateCount++;
                    certificate.AutoValidate = true;
                    certificate.IsValidated = true;
                    certificate.RawData = res.Export(X509ContentType.Pfx);
                    certificate.Thumbprint = res.Thumbprint;
                    certificate.ExpirationTime = res.NotAfter;
                    certificate.IssueTime = res.NotBefore;

                    certificate.ServerFarm!.UseHostName = true;
                    await serverConfigureService.SaveChangesAndInvalidateServerFarm(certificate.ProjectId,
                        certificate.ServerFarmId, true);
                    break;
                }

                logger.LogInformation("Acme validate in pending. ProjectId: {ProjectId}, CertificateId: {CertificateId}, CommonName: {CommonName}",
                    certificate.ProjectId, certificate.CertificateId, certificate.CommonName);
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
        catch (Exception ex) {
            certificate.ValidateError = ex.Message;
            certificate.ValidateErrorTime = DateTime.UtcNow;
            certificate.ValidateErrorCount++;
            logger.LogError("Could not validate a certificate.  ProjectId: {ProjectId}, CertificateId: {CertificateId}, CommonName: {CommonName}",
                certificate.ProjectId, certificate.CertificateId, certificate.CommonName);
        }
        finally {
            certificate.ValidateToken = null;
            certificate.ValidateKeyAuthorization = null;
            certificate.ValidateInprogress = false;
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

        var csr = new CertificateSigningRequest {
            CommonName = cn,
            Organization = o,
            OrganizationUnit = ou,
            LocationCountry = c,
            LocationState = st,
            LocationCity = l
        };

        return csr;
    }

    public async Task RunJob(CancellationToken cancellationToken)
    {
        logger.LogInformation("Certificates validation has been started...");

        // retryInterval is 90% AutoValidate interval to make sure we don't miss it
        var certificates = await vhRepo.CertificateExpiringList(
            certificateValidatorOptions.Value.ExpirationThreshold,
            certificateValidatorOptions.Value.MaxRetry,
            certificateValidatorOptions.Value.Interval * 0.9);

        logger.LogInformation("Validating certificates... CertificateCount: {CertificateCount}", 
            certificates.Length);

        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 10 };
        await Parallel.ForEachAsync(certificates, parallelOptions, async (certificate, ct) => {
            await ValidateJob(serviceScopeFactory, certificate.ProjectId, certificate.ServerFarmId, true, ct);
        });
    }
}
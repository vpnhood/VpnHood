using System.Security.Cryptography.X509Certificates;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos.Certificate;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.AccessServer.Services.Acme;

namespace VpnHood.AccessServer.Services;

public class CertificateService(
    VhRepo vhRepo,
    HttpClient httpClient,
    ServerConfigureService serverConfigureService,
    AcmeOrderFactory acmeOrderFactory)
{
    private readonly string _acmeAccountPem = "-----BEGIN EC PRIVATE KEY-----\r\nMHcCAQEEINeE2cCFoddl9OsZdjuJLerxSEQpJah55CwVJpHb2dbpoAoGCCqGSM49\r\nAwEHoUQDQgAEjSA/K3SIR7aiPjHxhQfA8y2+O5p6EgN2b1C3FmAzd2qMKY4cgTe0\r\nlntFDnWfY/mRqutw4K+m2QTxQlpgFiDpkQ==\r\n-----END EC PRIVATE KEY-----";
    private readonly string _acmeAccountPem2 = "-----BEGIN EC PRIVATE KEY-----\r\nMHcCAQEEIHzqNeXy5j0A4Rw7FuBnlANPk1aQ/WXhH/a3geGw2zrtoAoGCCqGSM49\r\nAwEHoUQDQgAE+SbDquCZ05EvXsJpW4Er4wHKb+eYyQWbmEtf4FutE/A0D1tH1STg\r\nUvtcnB46Ef1DTgPdeDnQbXONLL70YfvlXA==\r\n-----END EC PRIVATE KEY-----\r\n";

    public async Task<Certificate> Create(Guid projectId, Guid serverFarmId, CertificateCreateParams? createParams)
    {
        // make sure farm belong to this project
        var serverFarm = await vhRepo.ServerFarmGet(projectId, serverFarmId);

        // remove all farm certificates
        var certificates = await vhRepo.CertificateList(projectId, serverFarmId: serverFarmId);
        foreach (var cert in certificates)
            await vhRepo.CertificateDelete(projectId, cert.CertificateId);

        var certificate = CertificateHelper.BuildSelfSinged(serverFarm.ProjectId, serverFarmId: serverFarm.ServerFarmId, createParams: createParams);
        await vhRepo.AddAsync(certificate);
        await vhRepo.SaveChangesAsync();
        return certificate.ToDto();
    }

    public async Task<Certificate> Import(Guid projectId, Guid serverFarmId, CertificateImportParams importParams)
    {
        // make sure farm belong to this project
        var serverFarm = await vhRepo.ServerFarmGet(projectId, serverFarmId);

        // remove all farm certificates
        var certificates = await vhRepo.CertificateList(projectId, serverFarmId: serverFarmId);
        foreach (var cert in certificates)
            await vhRepo.CertificateDelete(projectId, cert.CertificateId);

        // replace with the new one
        var certificate = CertificateHelper.BuildByImport(serverFarm.ProjectId, serverFarm.ServerFarmId, importParams);
        await vhRepo.AddAsync(certificate);
        await vhRepo.SaveChangesAsync();
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

    public async Task<IEnumerable<Certificate>> List(Guid projectId, Guid? serverFarmId = null)
    {
        var models = await vhRepo.CertificateList(projectId, serverFarmId);
        return models.Select(x => x.ToDto());
    }

    public async Task Renew(Guid projectId, Guid certificateId, CancellationToken cancellationToken)
    {
        var certificate = await vhRepo.CertificateGet(projectId, certificateId, includeProjectAndLetsEncryptAccount: true);
        await Renew(certificate, cancellationToken);
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
            certificate.RenewInprogress = true;
            var x509Certificate = new X509Certificate2(certificate.RawData);
            var csr = CreateCsrFromCertificate(x509Certificate);

            // create account if not exists
            if (project.LetsEncryptAccount?.AccountPem == null)
            {
                var accountPem = await acmeOrderFactory.CreateNewAccount($"project-{project.ProjectId}@vpnhood.com");
                project.LetsEncryptAccount = new LetsEncryptAccount
                {
                    AccountPem = accountPem
                };
                await vhRepo.SaveChangesAsync();
            }

            // create order
            var acmeOrderService = await acmeOrderFactory.CreateOrder(project.LetsEncryptAccount.AccountPem, csr);
            certificate.RenewToken = acmeOrderService.Token;
            certificate.RenewKeyAuthorization = acmeOrderService.KeyAuthorization;
            await vhRepo.SaveChangesAsync();

            // wait for farm configuration
            await serverConfigureService.WaitForFarmConfiguration(certificate.ProjectId, certificate.ServerFarmId, cancellationToken);

            // validate order by manager
            await ValidateByManager(certificate);

            // Validate
            while (true)
            {
                var res = await acmeOrderService.Validate();
                if (res != null)
                {
                    certificate.RenewError = null;
                    certificate.RenewErrorTime = null;
                    certificate.RenewErrorCount = 0;
                    certificate.IsTrusted = true;
                    certificate.RawData = res.Export(X509ContentType.Pfx);
                    certificate.Thumbprint = res.Thumbprint;
                    certificate.ExpirationTime = res.NotAfter;
                    certificate.IssueTime = res.NotBefore;

                    await serverConfigureService.InvalidateServerFarm(certificate.ProjectId, certificate.ServerFarm.ServerFarmId, true);
                    break;
                }

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

    private async Task ValidateByManager(CertificateModel certificate)
    {
        var url = $"http://{certificate.CommonName}/.well-known/acme-challenge/{certificate.RenewToken}";
        var result = await httpClient.GetStringAsync(url);
        if (result != certificate.RenewKeyAuthorization)
            throw new Exception($"{certificate.CommonName} or the farm servers have not been configured properly. ");
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
}
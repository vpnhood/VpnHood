using Certes;
using Certes.Acme;
using Microsoft.AspNetCore.Builder.Extensions;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Clients;
using VpnHood.AccessServer.Dtos.ServerFarm;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Persistence.Enums;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.AccessServer.Persistence.Utils;

namespace VpnHood.AccessServer.Services;

public class CertificateSignerService(
    VhRepo vhRepo,
    IOptions<AppOptions> appOptions,
    AgentCacheClient agentCacheClient)
{
    private readonly string _acmeAccountPem = "-----BEGIN EC PRIVATE KEY-----\r\nMHcCAQEEINeE2cCFoddl9OsZdjuJLerxSEQpJah55CwVJpHb2dbpoAoGCCqGSM49\r\nAwEHoUQDQgAEjSA/K3SIR7aiPjHxhQfA8y2+O5p6EgN2b1C3FmAzd2qMKY4cgTe0\r\nlntFDnWfY/mRqutw4K+m2QTxQlpgFiDpkQ==\r\n-----END EC PRIVATE KEY-----";
    private readonly string _acmeAccountPem2 = "-----BEGIN EC PRIVATE KEY-----\r\nMHcCAQEEIHzqNeXy5j0A4Rw7FuBnlANPk1aQ/WXhH/a3geGw2zrtoAoGCCqGSM49\r\nAwEHoUQDQgAE+SbDquCZ05EvXsJpW4Er4wHKb+eYyQWbmEtf4FutE/A0D1tH1STg\r\nUvtcnB46Ef1DTgPdeDnQbXONLL70YfvlXA==\r\n-----END EC PRIVATE KEY-----\r\n";
    public async Task<string> CreateAccount()
    {
        return _acmeAccountPem;

        var acme = new AcmeContext(WellKnownServers.LetsEncryptStagingV2);
        var account = await acme.NewAccount(null, true);
        await acme.Authorization(account.Location).Challenges();
        var pemKey = acme.AccountKey.ToPem();
        return pemKey;
    }

    public async Task<string> NewOrder(string accountPem, string domain)
    {
        var accountKey = KeyFactory.FromPem(accountPem);
        var acme = new AcmeContext(WellKnownServers.LetsEncryptStagingV2, accountKey);
        var order = await acme.NewOrder(new[] { domain });
        return order.Location.ToString();
    }

    public async Task<string?> DnsText(string accountPem, string orderId, string certificateModelCommonName)
    {
        var accountKey = KeyFactory.FromPem(accountPem);
        var acme = new AcmeContext(WellKnownServers.LetsEncryptStagingV2, accountKey);
        var order = acme.Order(new Uri(orderId));
        var authorizations = await order.Authorizations();
        var dnsChallenge = await authorizations.Single().TlsAlpn();
        //dnsChallenge.KeyAuthz
        var dnsTxt = acme.AccountKey.DnsTxt(dnsChallenge.Token);
        return dnsTxt;
    }

    public async Task ValidateCertificate(CertificateModel certificate)
    {
        //vhRepo.CertificateGet()
        // CreateAccount

        // CreateOrder

        // CreateChallenge

        // Broadcast to all active servers

        // Wait for all active servers

        // Validate

        // Result
    }

    public async Task WaitForFarmConfiguration(ServerFarmModel serverFarm, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var servers = await agentCacheClient.GetServers(serverFarm.ProjectId, serverFarmId: serverFarm.ServerFarmId);
            if (servers.All(server => server.ServerState != ServerState.Configuring))
                break;

            await Task.Delay(appOptions.Value.ServerUpdateStatusInterval / 3, cancellationToken);
        }
    }
}
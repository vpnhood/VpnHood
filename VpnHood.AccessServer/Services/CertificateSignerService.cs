using Certes.Acme;
using Certes;
using VpnHood.AccessServer.Dtos.Certificate;

namespace VpnHood.AccessServer.Services;

public class CertificateSignerService
{
    private readonly string _acmeAccountPem = "-----BEGIN EC PRIVATE KEY-----\r\nMHcCAQEEINeE2cCFoddl9OsZdjuJLerxSEQpJah55CwVJpHb2dbpoAoGCCqGSM49\r\nAwEHoUQDQgAEjSA/K3SIR7aiPjHxhQfA8y2+O5p6EgN2b1C3FmAzd2qMKY4cgTe0\r\nlntFDnWfY/mRqutw4K+m2QTxQlpgFiDpkQ==\r\n-----END EC PRIVATE KEY-----";

    private AcmeContext GetAcmeContext()
    {
        var accountKey = KeyFactory.FromPem(_acmeAccountPem);
        var acme = new AcmeContext(WellKnownServers.LetsEncryptStagingV2, accountKey);
        return acme;
    }

    public async Task NewOrder(CertificateSigningRequest csr)
    {
        var acme = GetAcmeContext();
        var order = await acme.NewOrder(new[] { csr.CommonName });
    }
}
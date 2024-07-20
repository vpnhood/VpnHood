using VpnHood.AccessServer.Persistence.Models;

namespace VpnHood.AccessServer.Persistence.Utils;

public static class FarmModelExtensions
{
    public static CertificateModel GetCertificateInToken(this ServerFarmModel serverFarm)
    {
        ArgumentNullException.ThrowIfNull(serverFarm.Certificates);
        return serverFarm.Certificates.FirstOrDefault(x => x.IsInToken) 
               ?? throw new InvalidOperationException("The farm must have at least one certificate in token.");
    }
}
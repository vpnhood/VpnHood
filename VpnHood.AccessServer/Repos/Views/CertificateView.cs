using GrayMint.Common.Utils;
using VpnHood.AccessServer.Persistence.Models;

namespace VpnHood.AccessServer.Repos.Views;

public class CertificateView
{
    public required CertificateModel Certificate { get; init; }
    public required IEnumerable<IdName<Guid>>? ServerFarms { get; init; }
}
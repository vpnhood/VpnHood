using GrayMint.Common.Utils;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Persistence.Views;

public class CertificateView
{
    public required CertificateModel Certificate { get; init; }
    public required IEnumerable<IdName<Guid>>? ServerFarms { get; init; }
}
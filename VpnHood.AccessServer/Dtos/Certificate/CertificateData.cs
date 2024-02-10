using GrayMint.Common.Utils;

namespace VpnHood.AccessServer.Dtos.Certificate;

public class CertificateData
{
    public required Certificate Certificate { get; init; }
    public required IEnumerable<IdName<Guid>>? ServerFarms { get; init; }
}
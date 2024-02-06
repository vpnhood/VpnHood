namespace VpnHood.AccessServer.Dtos;
using GrayMint.Common.Utils;

public class CertificateData
{
    public required Certificate Certificate { get; init; }
    public required IEnumerable<IdName<Guid>>? ServerFarms { get; init; }
}
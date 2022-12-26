using GrayMint.Common.Utils;

namespace VpnHood.AccessServer.Dtos;

public class CertificateUpdateParams
{
    public Patch<byte[]>? RawData { get; set; }
    public Patch<string>? Password { get; set; }
}
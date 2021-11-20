namespace VpnHood.AccessServer.DTOs
{
    public class CertificateUpdateParams
    {
        public Patch<byte[]>? RawData { get; set; }
        public Patch<string>? Password { get; set; }
    }
}
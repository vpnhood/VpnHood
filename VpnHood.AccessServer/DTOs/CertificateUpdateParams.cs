namespace VpnHood.AccessServer.DTOs
{
    public class CertificateUpdateParams
    {
        public Wise<byte[]>? RawData { get; set; }
        public Wise<string>? Password { get; set; }
    }
}
using Microsoft.IdentityModel.Tokens;

namespace VpnHood.AccessServer.Settings
{
   public class AuthProviderItem
    {
        public string Name { get; set; }
        public string[] Issuers { get; set; }
        public string[] ValidAudiences { get; set; }
        public string SignatureValidatorUrl { get; set; }
        public string NameClaimType { get; set; }
        public string X509CertificateFile { get; set; }
    }
}

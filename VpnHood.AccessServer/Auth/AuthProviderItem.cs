using Microsoft.IdentityModel.Tokens;
using System;
using VpnHood.Common;

namespace VpnHood.AccessServer.Settings
{
    public class AuthProviderItem
    {
        public string Schema { get; set; }
        public string[] Issuers { get; set; }
        public string[] ValidAudiences { get; set; }
        public string? SignatureValidatorUrl { get; set; }
        public string NameClaimType { get; set; }
        public string? X509CertificateFile { get; set; }
        public string? SymmetricSecurityKey { get; set; }

        public AuthProviderItem(string schema, string nameClaimType, string[] validAudiences, string[] issuers)
        {
            if (string.IsNullOrEmpty(schema)) throw new ArgumentException("Parameter can not be empty!", nameof(schema));
            if (string.IsNullOrEmpty(nameClaimType)) throw new ArgumentException("Parameter can not be empty!", nameof(nameClaimType));
            if (Util.IsNullOrEmpty(validAudiences)) throw new ArgumentException("Parameter can not be empty!", nameof(validAudiences));
            if (Util.IsNullOrEmpty(issuers)) throw new ArgumentException("Parameter can not be empty!", nameof(issuers));

            Schema = schema;
            NameClaimType = nameClaimType;
            Issuers = issuers;
            ValidAudiences = validAudiences;
        }
    }
}

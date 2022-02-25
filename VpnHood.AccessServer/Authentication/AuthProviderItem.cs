using System;

namespace VpnHood.AccessServer.Authentication;

public class AuthProviderItem
{
    public AuthProviderItem()
    {
        // IConfiguration.GetSession need this constructor for serialization
    }

    public AuthProviderItem(string schema, string nameClaimType, string[] validAudiences, string[] issuers)
    {
        if (string.IsNullOrWhiteSpace(schema)) throw new ArgumentException(@$"'{nameof(schema)}' cannot be null or whitespace.", nameof(schema));
        if (string.IsNullOrEmpty(nameClaimType)) throw new ArgumentException(@$"'{nameof(nameClaimType)}' cannot be null or whitespace.", nameof(nameClaimType));
        if (validAudiences == null || validAudiences.Length == 0) throw new ArgumentException(@$"'{nameof(validAudiences)}' cannot be null or empty.", nameof(validAudiences));
        if (issuers == null || issuers.Length == 0) throw new ArgumentException(@$"'{nameof(issuers)}' cannot be null or empty.", nameof(issuers));

        Schema = schema;
        NameClaimType = nameClaimType;
        Issuers = issuers;
        ValidAudiences = validAudiences;
    }

    public string Schema { get; set; } = null!;
    public string[] Issuers { get; set; } = null!;
    public string[] ValidAudiences { get; set; } = null!;
    public string? SignatureValidatorUrl { get; set; }
    public string NameClaimType { get; set; } = null!;
    public string? X509CertificateFile { get; set; }
    public string? SymmetricSecurityKey { get; set; } //IConfigurationSection can not read binary!
}

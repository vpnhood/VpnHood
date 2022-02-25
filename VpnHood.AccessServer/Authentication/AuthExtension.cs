using System;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace VpnHood.AccessServer.Authentication;

internal static class AuthExtension
{
    public static AuthenticationBuilder AddAppAuthentication(this AuthenticationBuilder auth, IConfigurationSection configurationSection)
    {
        var authProviderItems = configurationSection.Get<AuthProviderItem[]>() ?? Array.Empty<AuthProviderItem>();
        foreach (var item in authProviderItems)
        {
            // set security key
            SecurityKey? securityKey = null;
            if (!string.IsNullOrEmpty(item.X509CertificateFile))
            {
                if (!string.IsNullOrEmpty(item.SymmetricSecurityKey))
                    throw new Exception($"{nameof(item.X509CertificateFile)} and {nameof(item.SymmetricSecurityKey)} can not be set together!");
                var cert = new X509Certificate2(item.X509CertificateFile);
                securityKey = new X509SecurityKey(cert);
            }

            else if (item.SymmetricSecurityKey != null)
            {
                securityKey = new SymmetricSecurityKey(Convert.FromBase64String(item.SymmetricSecurityKey));
            }

            else if (string.IsNullOrEmpty(item.SignatureValidatorUrl))
            {
                throw new Exception("There is no signing method!");
            }

            auth.AddJwtBearer(item.Schema, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    NameClaimType = item.NameClaimType,
                    RequireSignedTokens = securityKey != null,
                    IssuerSigningKey = securityKey,
                    ValidIssuers = item.Issuers,
                    ValidAudiences = item.ValidAudiences,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateIssuer = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(TokenValidationParameters.DefaultClockSkew.TotalSeconds),
                };
                if (item.SignatureValidatorUrl != null)
                    options.SecurityTokenValidators.Add(new AuthSecurityTokenValidator(item.SignatureValidatorUrl));
            });
        }

        return auth;
    }

}
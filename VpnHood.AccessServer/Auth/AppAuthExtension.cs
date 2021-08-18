using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Security.Cryptography.X509Certificates;
using VpnHood.AccessServer.Settings;

namespace VpnHood.AccessServer.Auth
{
    static class AppAuthExtension
    {
        public static IApplicationBuilder UseAppAuthentication(this IApplicationBuilder app, AuthProviderItem[] authProviderSettings)
        {
            return app.UseMiddleware<AppAuthentication>((object)authProviderSettings);
        }

        public static IServiceCollection AddAppAuthentication(this IServiceCollection services, AuthProviderItem[] authProviderSettings)
        {
            var auth = services.AddAuthentication();
            foreach (var item in authProviderSettings)
            {

                // set security key
                SecurityKey? securityKey = null;
                if (!string.IsNullOrEmpty(item.X509CertificateFile))
                {
                    if (!string.IsNullOrEmpty(item.SymmetricSecurityKey)) throw new Exception($"{nameof(item.X509CertificateFile)} and {nameof(item.SymmetricSecurityKey)} can not be set together!");
                    var cert = new X509Certificate2(item.X509CertificateFile);
                    securityKey = new X509SecurityKey(cert);
                }
                else if (item.SymmetricSecurityKey != null)
                    securityKey = new SymmetricSecurityKey(Convert.FromBase64String(item.SymmetricSecurityKey));

                else if (string.IsNullOrEmpty(item.SignatureValidatorUrl))
                    throw new Exception("There is no signing method!");

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
                            ClockSkew = TimeSpan.FromSeconds(TokenValidationParameters.DefaultClockSkew.TotalSeconds)
                        };
                        if (item.SignatureValidatorUrl != null)
                            options.SecurityTokenValidators.Add(new AuthSecurityTokenValidator(item.SignatureValidatorUrl));
                    });
            }

            return services;
        }
    }
}

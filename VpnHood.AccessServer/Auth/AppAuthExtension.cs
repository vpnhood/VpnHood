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
                //load certificate if exists
                X509SecurityKey issuerSigningKey = null;
                if (item.X509CertificateFile != null)
                {
                    var cert = new X509Certificate2(item.X509CertificateFile);
                    issuerSigningKey = new X509SecurityKey(cert);
                }

                auth.AddJwtBearer(item.Name, options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        NameClaimType = item.NameClaimType,
                        RequireSignedTokens = issuerSigningKey != null,
                        IssuerSigningKey = issuerSigningKey,
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

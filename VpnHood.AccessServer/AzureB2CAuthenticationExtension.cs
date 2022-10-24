using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Identity.Web;

namespace VpnHood.AccessServer;

public static class AzureB2CAuthenticationExtension
{
    public static AuthenticationBuilder AddAzureB2CAuthentication(this AuthenticationBuilder authenticationBuilder,
        IConfigurationSection configurationSection)
    {
        authenticationBuilder.AddMicrosoftIdentityWebApi(jwtBearerOptions =>
            {
                jwtBearerOptions.Events = new JwtBearerEvents()
                {
                    OnTokenValidated = context =>
                    {
                        var claimsIdentity = new ClaimsIdentity();
                        var email = context.Principal?.Claims.FirstOrDefault(claim => claim.Type == "emails")?.Value;
                        if (email != null)
                        {
                            claimsIdentity.AddClaim(new Claim(ClaimTypes.Email, email));
                            context.Principal!.AddIdentity(claimsIdentity);
                        }

                        return Task.CompletedTask;
                    }
                };
            },
            microsoftIdentityOptions => { configurationSection.Bind(microsoftIdentityOptions); }, "AzureB2C");
        return authenticationBuilder;
    }
}
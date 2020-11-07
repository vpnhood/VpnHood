using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;

namespace VpnHood.AccessServer.Auth
{
    public class AuthSecurityTokenValidator : JwtSecurityTokenHandler
    {
        private readonly string _signatureValidatorUrl;

        public AuthSecurityTokenValidator(string signatureValidatorUrl)
        {
            _signatureValidatorUrl = signatureValidatorUrl;
        }

        protected override JwtSecurityToken ValidateSignature(string token, TokenValidationParameters validationParameters)
        {
            var httpClient = new HttpClient();
            if (!httpClient.GetAsync(string.Format(_signatureValidatorUrl, token)).Result.IsSuccessStatusCode)
                throw new SecurityTokenInvalidSignatureException();

           return ReadJwtToken(token);
        }
    }
}

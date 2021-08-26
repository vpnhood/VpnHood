using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace VpnHood.AccessServer.Cmd
{
    class JwtTool
    {
        public static string CreateSymJwt(Aes aes, string issuer, string audience, string subject, Claim[] claims = null)
        {
            List<Claim> claimsList = new List<Claim>
            {
                new Claim("sub", subject)
            };
            if (claims != null)
                claimsList.AddRange(claims);

            // create token
            var secKey = new SymmetricSecurityKey(aes.Key);
            var signingCredentials = new SigningCredentials(secKey, SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(issuer: issuer,
                claims: claimsList.ToArray(),
                audience: audience,
                expires: DateTime.Now.AddYears(10),
                signingCredentials: signingCredentials);

            var handler = new JwtSecurityTokenHandler();
            var ret = handler.WriteToken(token);
            return ret;
        }
    }
}

using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;

namespace VpnHood.AccessServer.Cmd
{
    class JwtTool
    {
        public static string CreateSymJwt(Aes aes, string issuer, string audience, string subject, string role)
        {
            var claims = new List<Claim>
            {
                new Claim("sub", subject)
            };
            if (!string.IsNullOrEmpty(role))
            {
                claims.Add(new Claim("roles", role));
            }

            // create token
            var secKey = new SymmetricSecurityKey(aes.Key);
            var signingCredentials = new SigningCredentials(secKey, SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(issuer: issuer,
                claims: claims.ToArray(),
                audience: audience,
                expires: DateTime.Now.AddYears(10),
                signingCredentials: signingCredentials);

            var handler = new JwtSecurityTokenHandler();
            var ret = handler.WriteToken(token);
            return ret;
        }
    }
}

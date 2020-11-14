using Microsoft.IdentityModel.Tokens;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;

namespace VpnHood.Server.App
{
    class JwtTool
    {
        public static void CreateJwt(string issuer, string audience, string subject)
        {
            // create key
            var aes = Aes.Create();
            aes.KeySize = 256;
            aes.GenerateKey();
            Console.WriteLine("Key: " + Convert.ToBase64String(aes.Key));
            var encKey = aes.Key;

            // create token
            var key = new SymmetricSecurityKey(encKey);
            var signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(issuer: issuer,
                claims: new Claim[] { new Claim("sub", subject), new Claim(ClaimTypes.Role, "admin") },
                audience: audience,
                expires: DateTime.Now.AddYears(10),
                signingCredentials: signingCredentials);

            Console.WriteLine(token);
            var handler = new JwtSecurityTokenHandler();
            var str = handler.WriteToken(token);
            Console.WriteLine("Token: " + str);
        }
    }
}

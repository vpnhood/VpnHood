using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Data.SqlClient;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Services;

namespace VpnHood.AccessServer.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class AccessTokenController : SuperController<AccessTokenController>
    {
        public AccessTokenController(ILogger<AccessTokenController> logger) : base(logger)
        {
        }

        private static async Task<Token> TokenFromAccessToken(AccessToken accessToken)
        {
            var certificateService = CertificateService.FromId(accessToken.serverEndPoint);
            var certificate = await certificateService.Get();
            var x509Certificate = new X509Certificate2(certificate.rawData);

            var token = new Token()
            {
                Version = 1,
                TokenId = accessToken.accessTokenId,
                Name = accessToken.accessTokenName,
                SupportId = accessToken.supportId,
                Secret = accessToken.secret,
                ServerEndPoint = accessToken.serverEndPoint,
                IsPublic = accessToken.isPublic,
                DnsName = x509Certificate.GetNameInfo(X509NameType.DnsName, false),
                PublicKeyHash = Token.ComputePublicKeyHash(x509Certificate.GetPublicKey()),
            };
            return token;
        }

        [HttpPost]
        [Route(nameof(CreatePublic))]
        [Authorize(AuthenticationSchemes = "auth", Roles = "Admin")]
        public async Task<AccessToken> CreatePublic(string serverEndPoint, string tokenName, int maxTraffic)
        {
            var accessTokenService = await AccessTokenService.CreatePublic(serverEndPoint: serverEndPoint, tokenName: tokenName, maxTraffic: maxTraffic);
            var accessToken = await accessTokenService.GetAccessToken();
            return accessToken;
        }

        [HttpPost]
        [Route(nameof(CreatePrivate))]
        [Authorize(AuthenticationSchemes = "auth", Roles = "Admin")]
        public async Task<AccessToken> CreatePrivate(string serverEndPoint, string tokenName, int maxTraffic, int maxClient, DateTime? endTime = null, int lifetime = 0)
        {
            var accessTokenService = await AccessTokenService.CreatePrivate(
                serverEndPoint: serverEndPoint, tokenName: tokenName, maxTraffic: maxTraffic,
                maxClient: maxClient, endTime: endTime, lifetime: lifetime);

            var accessToken = await accessTokenService.GetAccessToken();
            return accessToken;
        }

        [HttpGet]
        [Route(nameof(GetAccessKey))]
        [Authorize(AuthenticationSchemes = "auth", Roles = "Admin")]
        public async Task<string> GetAccessKey(Guid accessTokenId)
        {
            var accessTokenService = AccessTokenService.FromId(accessTokenId);
            var accessToken = await accessTokenService.GetAccessToken();
            var token = await TokenFromAccessToken(accessToken);
            return token.ToAccessKey();
        }


        [HttpGet]
        [Route(nameof(GetAccessToken))]
        [Authorize(AuthenticationSchemes = "auth", Roles = "Admin")]
        public Task<AccessToken> GetAccessToken(Guid accessTokenId)
        {
            var accessTokenService = AccessTokenService.FromId(accessTokenId);
            return accessTokenService.GetAccessToken();
        }
    }
}

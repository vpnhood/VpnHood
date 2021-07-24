using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Services;
using VpnHood.Common;

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
            var certificateService = ServerEndPointService.FromId(accessToken.serverEndPoint);
            var certificate = await certificateService.Get();
            var x509Certificate = new X509Certificate2(certificate.certificateRawData);

            var token = new Token()
            {
                Version = 1,
                TokenId = accessToken.accessTokenId,
                Name = accessToken.accessTokenName,
                SupportId = accessToken.supportId,
                Secret = accessToken.secret,
                ServerEndPoint = IPEndPoint.Parse(accessToken.serverEndPoint),
                IsPublic = accessToken.endPointGroupId == 0,
                DnsName = x509Certificate.GetNameInfo(X509NameType.DnsName, false),
                CertificateHash = x509Certificate.GetCertHash(),
                Url = accessToken.url,
            };
            return token;
        }

        [HttpPost]
        [Route(nameof(Create))]
        [Authorize(AuthenticationSchemes = "auth", Roles = "Admin")]
        public async Task<AccessToken> Create(string tokenName = null, int serverEndPointGroupId = 0, int maxTraffic = 0, int maxClient = 0, DateTime? endTime = null, int lifetime = 0, string tokenUrl = null)
        {
            var accessTokenService = await AccessTokenService.Create(serverEndPointGroupId: serverEndPointGroupId, tokenName: tokenName, maxTraffic: maxTraffic,
                maxClient: maxClient, endTime: endTime, lifetime: lifetime, tokenUrl: tokenUrl);

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

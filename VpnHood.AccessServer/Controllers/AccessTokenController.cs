using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using VpnHood.AccessServer.Models;
using VpnHood.Common;
using VpnHood.Server;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace VpnHood.AccessServer.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [Authorize(AuthenticationSchemes = "auth", Roles = "Admin")]
    public class AccessTokenController : SuperController<AccessTokenController>
    {
        public AccessTokenController(ILogger<AccessTokenController> logger) : base(logger)
        {
        }

        [HttpPost]
        [Route(nameof(Create))]
        public async Task<AccessToken> Create(Guid? accessTokenGroupId = null, string tokenName = null,
            int maxTraffic = 0, int maxClient = 0,
            DateTime? endTime = null, int lifetime = 0,
            bool isPublic = false, string tokenUrl = null)
        {
            // find default serveEndPoint 
            using VhContext vhContext = new();
            if (accessTokenGroupId == null)
                accessTokenGroupId = (await vhContext.AccessTokenGroups.SingleAsync(x => x.IsDefault)).AccessTokenGroupId;

            // create support id
            var supportCode = (await vhContext.AccessTokens.DefaultIfEmpty().MaxAsync(x => (int?)x.SupportCode)) ?? 1000;
            supportCode++;

            Aes aes = Aes.Create();
            aes.KeySize = 128;
            aes.GenerateKey();

            AccessToken accessToken = new()
            {
                AccessTokenId = Guid.NewGuid(),
                AccessTokenGroupId = accessTokenGroupId.Value,
                AccessTokenName = tokenName,
                MaxTraffic = maxTraffic,
                MaxClient = maxClient,
                EndTime = endTime,
                Lifetime = lifetime,
                Url = tokenUrl,
                IsPublic = isPublic,
                Secret = aes.Key,
                SupportCode = supportCode //todo: test for just increase for each accound
            };

            await vhContext.AccessTokens.AddAsync(accessToken);
            await vhContext.SaveChangesAsync();
            return accessToken;
        }

        [HttpGet]
        [Route(nameof(GetAccessKey))]
        public async Task<string> GetAccessKey(Guid accessTokenId)
        {
            // get accessToken with default endPoint
            using VhContext vhContext = new();

            var query = from AC in vhContext.Accounts
                        join ATG in vhContext.AccessTokenGroups on AC.AccountId equals ATG.AccountId
                        join AT in vhContext.AccessTokens on ATG.AccessTokenGroupId equals AT.AccessTokenGroupId
                        join EP in vhContext.ServerEndPoints on ATG.AccessTokenGroupId equals EP.AccessTokenGroupId
                        where AC.AccountId == AccountId && AT.AccessTokenId == accessTokenId && EP.IsDefault
                        select new { AT, EP };
            var result = await query.SingleAsync();


            var accessToken = result.AT;
            var serverEndPoint = result.EP;
            var x509Certificate = new X509Certificate2(serverEndPoint.CertificateRawData);

            // create token
            var token = new Token()
            {
                Version = 1,
                TokenId = accessToken.AccessTokenId,
                Name = accessToken.AccessTokenName,
                SupportId = accessToken.SupportCode,
                Secret = accessToken.Secret,
                ServerEndPoint = IPEndPoint.Parse(serverEndPoint.PulicEndPoint),
                IsPublic = accessToken.IsPublic,
                DnsName = x509Certificate.GetNameInfo(X509NameType.DnsName, false),
                CertificateHash = x509Certificate.GetCertHash(),
                Url = accessToken.Url,
            };

            return token.ToAccessKey();
        }


        [HttpGet]
        [Route(nameof(GetAccessToken))]
        public async Task<AccessToken> GetAccessToken(Guid accessTokenId)
        {
            using VhContext vhContext = new();
            return await vhContext.AccessTokens.SingleAsync(e => e.AccessTokenId == accessTokenId);
        }

        [HttpGet]
        [Route(nameof(GetAccessUsage))]
        public async Task<AccessUsage> GetAccessUsage(ClientIdentity clientIdentity)
        {
            using VhContext vhContext = new();
            var accessToken = await vhContext.AccessTokens.SingleAsync(e => e.AccessTokenId == clientIdentity.TokenId);
            var clientId = accessToken.IsPublic ? clientIdentity.ClientId : Guid.Empty;
            var accessUsage = await vhContext.AccessUsages.FindAsync(clientIdentity.TokenId, clientId);
            return accessUsage ?? new AccessUsage { AccessTokenId = clientIdentity.TokenId, ClientId = clientIdentity.ClientId };
        }

    }
}

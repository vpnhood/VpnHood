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
        public async Task<AccessToken> Create(Guid? serverEndPointGroupId = null, string tokenName = null, 
            int maxTraffic = 0, int maxClient = 0, 
            DateTime? endTime = null, int lifetime = 0, 
            bool isPublic = false, string tokenUrl = null)
        {
            // find default serveEndPoint 
            using VhContext vhContext = new();
            if (serverEndPointGroupId == null)
                serverEndPointGroupId = (await vhContext.ServerEndPointGroups.SingleAsync(x => x.IsDefault)).ServerEndPointGroupId;

            AccessToken accessToken = new()
            {
                ServerEndPointGroupId = serverEndPointGroupId.Value,
                AccessTokenName = tokenName,
                MaxTraffic = maxTraffic,
                MaxClient = maxClient,
                EndTime = endTime,
                Lifetime = lifetime,
                Url = tokenUrl,
                IsPublic = isPublic, 
                SupportId = await vhContext.AccessTokens.MaxAsync(x=>x.SupportId) //todo: test for just increase for each accound
            };

            vhContext.AccessTokens.Add(accessToken);
            await vhContext.SaveChangesAsync();
            return accessToken;
        }

        [HttpGet]
        [Route(nameof(GetAccessKey))]
        public async Task<string> GetAccessKey(Guid accessTokenId)
        {
            // get accessToken with default endPoint
            using VhContext vhContext = new();

            var res = await (from AT in vhContext.AccessTokens
                             join EP in vhContext.ServerEndPoints on AT.ServerEndPointGroupId equals EP.ServerEndPointGroupId
                             where AT.AccessTokenId == accessTokenId
                             select new { AT, EP }).SingleAsync();

            var accessToken = res.AT;
            var serverEndPoint = res.EP;
            var x509Certificate = new X509Certificate2(serverEndPoint.CertificateRawData);

            // create token
            var token = new Token()
            {
                Version = 1,
                TokenId = accessToken.AccessTokenId,
                Name = accessToken.AccessTokenName,
                SupportId = accessToken.SupportId,
                Secret = accessToken.Secret,
                ServerEndPoint = IPEndPoint.Parse(serverEndPoint.ServerEndPointId),
                IsPublic = accessToken.IsPublic,
                DnsName = x509Certificate.GetNameInfo(X509NameType.DnsName, false),
                CertificateHash = x509Certificate.GetCertHash(),
                Url = accessToken.Url,
            };

            return token.ToAccessKey();
        }


        [HttpGet]
        [Route(nameof(GetAccessToken))]
        public Task<AccessToken> GetAccessToken(Guid accessTokenId)
        {
            using VhContext vhContext = new();
            return vhContext.AccessTokens.SingleAsync(e => e.AccessTokenId == accessTokenId);
        }

        [HttpGet]
        [Route(nameof(GetAccessUsage))]
        public async Task<AccessUsage> GetAccessUsage(ClientIdentity clientIdentity)
        {
            using VhContext vhContext = new();
            var accessToken = await vhContext.AccessTokens.SingleAsync(e => e.AccessTokenId == clientIdentity.TokenId);
            var clientId = accessToken.IsPublic ? clientIdentity.ClientId : Guid.Empty;
            var accessUsage = await vhContext.AccessUsages.FindAsync(clientIdentity.TokenId, clientId);
            return accessUsage ?? new AccessUsage { AccessTokenId  = clientIdentity.TokenId, ClientId = clientIdentity.ClientId };
        }

    }
}

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
using System.Text.Json;
using System.Collections.Generic;

namespace VpnHood.AccessServer.Controllers
{
    [ApiController]
    [Route("{accountId}/[controller]s")]
    [Authorize(AuthenticationSchemes = "auth", Roles = "Admin")]
    public class AccessTokenController : SuperController<AccessTokenController>
    {
        public AccessTokenController(ILogger<AccessTokenController> logger) : base(logger)
        {
        }

        [HttpPost]
        public async Task<AccessToken> Create(Guid accountId,
            Guid? accessTokenGroupId = null,
            string tokenName = null,
            int maxTraffic = 0,
            int maxClient = 0,
            DateTime? endTime = null,
            int lifetime = 0,
            bool isPublic = false,
            string tokenUrl = null)
        {
            // find default serveEndPoint 
            using VhContext vhContext = new();
            if (accessTokenGroupId == null)
                accessTokenGroupId = (await vhContext.AccessTokenGroups.SingleAsync(x => x.AccountId == accountId && x.IsDefault)).AccessTokenGroupId;
            else
                await vhContext.AccessTokenGroups.SingleAsync(x => x.AccountId == accountId && x.AccessTokenGroupId == accessTokenGroupId);

            // create support id
            var supportCode = (await vhContext.AccessTokens.Where(x => x.AccountId == accountId).MaxAsync(x => (int?)x.SupportCode)) ?? 1000;
            supportCode++;

            Aes aes = Aes.Create();
            aes.KeySize = 128;
            aes.GenerateKey();

            AccessToken accessToken = new()
            {
                AccessTokenId = Guid.NewGuid(),
                AccountId = accountId,
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


        [HttpPut()]
        [Route("{accessTokenId}")]
        public async Task<AccessToken> Update(Guid accountId, Guid accessTokenId, AccessToken accessToken)
        {
            if (accountId != accessToken.AccountId || accessTokenId != accessToken.AccessTokenId)
                throw new InvalidOperationException();

            using VhContext vhContext = new();

            // validate accessToken.AccessTokenGroupId
            await vhContext.AccessTokenGroups.SingleAsync(x => x.AccountId == accountId && x.AccessTokenGroupId == accessToken.AccessTokenGroupId);

            // validate updatable
            var newItem = await vhContext.AccessTokens.SingleAsync(x => x.AccountId == accountId && x.AccessTokenId == accessTokenId);
            if (newItem.IsPublic != accessToken.IsPublic) throw new InvalidOperationException($"Could not change {nameof(newItem)}");
            if (newItem.StartTime != accessToken.StartTime) throw new InvalidOperationException($"Could not change {nameof(newItem.StartTime)}");
            if (newItem.SupportCode != accessToken.SupportCode) throw new InvalidOperationException($"Could not change {nameof(newItem.SupportCode)}");
            if (newItem.Secret != null && !Enumerable.SequenceEqual(newItem.Secret, accessToken.Secret)) throw new InvalidOperationException($"Could not change {nameof(newItem.Secret)}");

            // update
            newItem.AccessTokenGroupId = accessToken.AccessTokenGroupId;
            newItem.AccessTokenName = accessToken.AccessTokenName;
            newItem.EndTime = accessToken.EndTime;
            newItem.Lifetime = accessToken.Lifetime;
            newItem.MaxClient = accessToken.MaxClient;
            newItem.MaxTraffic = accessToken.MaxTraffic;
            newItem.Url = accessToken.Url;
            vhContext.AccessTokens.Update(newItem);

            await vhContext.SaveChangesAsync();
            return newItem;
        }


        [HttpGet]
        [Route("{accessTokenId}/AccessKey")]
        public async Task<string> GetAccessKey(Guid accountId, Guid accessTokenId)
        {
            // get accessToken with default endPoint
            using VhContext vhContext = new();

            var query = from AC in vhContext.Accounts
                        join ATG in vhContext.AccessTokenGroups on AC.AccountId equals ATG.AccountId
                        join AT in vhContext.AccessTokens on ATG.AccessTokenGroupId equals AT.AccessTokenGroupId
                        join EP in vhContext.ServerEndPoints on ATG.AccessTokenGroupId equals EP.AccessTokenGroupId
                        where AC.AccountId == accountId && AT.AccessTokenId == accessTokenId && EP.IsDefault
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
        [Route("{accessTokenId}")]
        public async Task<AccessToken> GetAccessToken(Guid accountId, Guid accessTokenId)
        {
            using VhContext vhContext = new();
            return await vhContext.AccessTokens.SingleAsync(e => e.AccountId == accountId && e.AccessTokenId == accessTokenId);
        }


        [HttpGet]
        [Route("{accessTokenId}/AccessTokenUsage")]
        public async Task<AccessUsage> GetAccessUsage(Guid accountId, Guid accessTokenId, Guid? clientId = null)
        {
            using VhContext vhContext = new();
            return await vhContext.AccessUsages
                .Include(x => x.Client)
                .Include(x => x.AccessToken)
                .Where(x => x.AccessToken.AccountId == accountId &&
                        x.AccessToken.AccessTokenId == accessTokenId &&
                        (!x.AccessToken.IsPublic || x.Client.ClientId == clientId))
                .SingleOrDefaultAsync();
        }

        [HttpGet]
        [Route("{accessTokenId}/AccessTokenUsageLogs")]
        public async Task<AccessUsageLog[]> GetAccessUsageLogs(Guid accountId, Guid? accessTokenId = null, Guid? clientId = null, int recordIndex = 0, int recordCount = 1000)
        {
            using VhContext vhContext = new();
            var query = vhContext.AccessUsageLogs
                .Include(x => x.Server)
                .Include(x => x.Client)
                .Include(x => x.AccessUsage)
                .Include(x => x.AccessUsage.AccessToken)
                .Where(x => x.AccessUsage.AccessToken.AccountId == accountId &&
                            x.AccessUsage.AccessTokenId == accessTokenId &&
                            x.Server != null && x.Client != null && x.AccessUsage != null && x.AccessUsage.AccessToken != null);

            if (accessTokenId != null)
                query = query
                    .Where(x => x.AccessUsage.AccessTokenId == accessTokenId);

            if (clientId != null)
                query = query
                    .Where(x => x.Client.ClientId == clientId);

            var res = await query
                .OrderByDescending(x => x.AccessUsageLogId)
                .Skip(recordIndex).Take(recordCount)
                .ToArrayAsync();

            return res;
        }
    }
}

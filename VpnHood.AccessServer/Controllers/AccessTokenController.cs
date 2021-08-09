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
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Collections.Generic;

namespace VpnHood.AccessServer.Controllers
{
    [Route("/api/projects/{projectId}/access-tokens")]
    [Authorize(AuthenticationSchemes = "auth", Roles = "Admin")]
    public class AccessTokenController : SuperController<AccessTokenController>
    {
        public AccessTokenController(ILogger<AccessTokenController> logger) : base(logger)
        {
        }

        [HttpPost]
        public async Task<AccessToken> Create(Guid projectId,
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
                accessTokenGroupId = (await vhContext.AccessTokenGroups.SingleAsync(x => x.ProjectId == projectId && x.IsDefault)).AccessTokenGroupId;
            else
                await vhContext.AccessTokenGroups.SingleAsync(x => x.ProjectId == projectId && x.AccessTokenGroupId == accessTokenGroupId);

            // create support id
            var supportCode = (await vhContext.AccessTokens.Where(x => x.ProjectId == projectId).MaxAsync(x => (int?)x.SupportCode)) ?? 1000;
            supportCode++;

            Aes aes = Aes.Create();
            aes.KeySize = 128;
            aes.GenerateKey();

            AccessToken accessToken = new()
            {
                AccessTokenId = Guid.NewGuid(),
                ProjectId = projectId,
                AccessTokenGroupId = accessTokenGroupId.Value,
                AccessTokenName = tokenName,
                MaxTraffic = maxTraffic,
                MaxClient = maxClient,
                EndTime = endTime,
                Lifetime = lifetime,
                Url = tokenUrl,
                IsPublic = isPublic,
                Secret = aes.Key,
                SupportCode = supportCode
            };

            await vhContext.AccessTokens.AddAsync(accessToken);
            await vhContext.SaveChangesAsync();
            return accessToken;
        }


        [HttpPut("{accessTokenId}")]
        public async Task<AccessToken> Update(Guid projectId, Guid accessTokenId, AccessToken accessToken)
        {
            if (projectId != accessToken.ProjectId || accessTokenId != accessToken.AccessTokenId)
                throw new InvalidOperationException();

            using VhContext vhContext = new();

            // validate accessToken.AccessTokenGroupId
            await vhContext.AccessTokenGroups.SingleAsync(x => x.ProjectId == projectId && x.AccessTokenGroupId == accessToken.AccessTokenGroupId);

            // validate updatable
            var newItem = await vhContext.AccessTokens.SingleAsync(x => x.ProjectId == projectId && x.AccessTokenId == accessTokenId);
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

        public class AccessKey
        {
            public string Key { get; set; }
        }

        [HttpGet("{accessTokenId}/access-key")]
        public async Task<AccessKey> GetAccessKey(Guid projectId, Guid accessTokenId)
        {
            // get accessToken with default endPoint
            using VhContext vhContext = new();

            var query = from AC in vhContext.Projects
                        join ATG in vhContext.AccessTokenGroups on AC.ProjectId equals ATG.ProjectId
                        join AT in vhContext.AccessTokens on ATG.AccessTokenGroupId equals AT.AccessTokenGroupId
                        join EP in vhContext.ServerEndPoints on ATG.AccessTokenGroupId equals EP.AccessTokenGroupId
                        where AC.ProjectId == projectId && AT.AccessTokenId == accessTokenId && EP.IsDefault
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

            return new AccessKey { Key = token.ToAccessKey() };
        }

        [HttpGet("{accessTokenId}")]
        public async Task<AccessTokenData> Get(Guid projectId, Guid accessTokenId)
        {
            var items = await List(projectId: projectId, accessTokenId: accessTokenId);
            return items.Single();
        }


        public class AccessTokenData
        {
            public AccessToken AccessToken { get; set; }
            /// <summary>
            /// The usage of the token or null if AccessToken is public.
            /// </summary>
            public AccessUsage AccessUsage { get; set; }
        }

        [HttpGet("list")]
        public async Task<AccessTokenData[]> List(Guid projectId, Guid? accessTokenId = null, Guid? accessTokenGroupId = null, int recordIndex = 0, int recordCount = 300)
        {
            using VhContext vhContext = new();
            var query = from AT in vhContext.AccessTokens.Include(x => x.AccessTokenGroup)
                        join AU in vhContext.AccessUsages on new { key1 = AT.AccessTokenId, key2 = AT.IsPublic } equals new { key1 = AU.AccessTokenId, key2 = false } into grouping
                        from AU in grouping.DefaultIfEmpty()
                        where AT.ProjectId == projectId && AT.AccessTokenGroup != null
                        select new AccessTokenData
                        {
                            AccessToken = AT,
                            AccessUsage = AU
                        };

            if (accessTokenId!=null)
                query = query.Where(x => x.AccessToken.AccessTokenId == accessTokenId);

            if (accessTokenGroupId != null)
                query = query.Where(x => x.AccessToken.AccessTokenGroupId == accessTokenGroupId);

            var res = await query
                .Skip(recordIndex)
                .Take(recordCount)
                .ToArrayAsync();

            return res;
        }

        [HttpGet("{accessTokenId}/usage")]
        public async Task<AccessUsage> GetAccessUsage(Guid projectId, Guid accessTokenId, Guid? clientId = null)
        {
            using VhContext vhContext = new();
            return await vhContext.AccessUsages
                .Include(x => x.Client)
                .Include(x => x.AccessToken)
                .Where(x => x.AccessToken.ProjectId == projectId &&
                        x.AccessToken.AccessTokenId == accessTokenId &&
                        (!x.AccessToken.IsPublic || x.Client.ClientId == clientId))
                .SingleOrDefaultAsync();
        }

        [HttpGet("{accessTokenId}/usage-logs")]
        public async Task<AccessUsageLog[]> GetAccessUsageLogs(Guid projectId, Guid? accessTokenId = null, Guid? clientId = null, int recordIndex = 0, int recordCount = 1000)
        {
            using VhContext vhContext = new();
            var query = vhContext.AccessUsageLogs
                .Include(x => x.Server)
                .Include(x => x.Client)
                .Include(x => x.AccessUsage)
                .Include(x => x.AccessUsage.AccessToken)
                .Where(x => x.AccessUsage.AccessToken.ProjectId == projectId &&
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

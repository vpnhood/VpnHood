using System;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.DTOs;
using VpnHood.AccessServer.Models;
using VpnHood.Common;

namespace VpnHood.AccessServer.Controllers
{
    [Route("/api/projects/{projectId:guid}/access-tokens")]
    [Authorize(AuthenticationSchemes = "auth", Roles = "Admin")]
    public class AccessTokenController : SuperController<AccessTokenController>
    {
        public AccessTokenController(ILogger<AccessTokenController> logger) : base(logger)
        {
        }

        [HttpPost]
        public async Task<AccessToken> Create(Guid projectId, AccessTokenCreateParams createParams)
        {
            // find default serveEndPoint 
            await using VhContext vhContext = new();
            if (createParams.AccessTokenGroupId == null)
                createParams.AccessTokenGroupId = (await vhContext.AccessTokenGroups.SingleAsync(x => x.ProjectId == projectId && x.IsDefault)).AccessTokenGroupId;
            else
                await vhContext.AccessTokenGroups.SingleAsync(x => x.ProjectId == projectId && x.AccessTokenGroupId == createParams.AccessTokenGroupId);

            // create support id
            var supportCode = await vhContext.AccessTokens.Where(x => x.ProjectId == projectId).MaxAsync(x => (int?)x.SupportCode) ?? 1000;
            supportCode++;

            Aes aes = Aes.Create();
            aes.KeySize = 128;
            aes.GenerateKey();

            AccessToken accessToken = new()
            {
                AccessTokenId = createParams.AccessTokenId ?? Guid.NewGuid(),
                ProjectId = projectId,
                AccessTokenGroupId = createParams.AccessTokenGroupId.Value,
                AccessTokenName = createParams.AccessTokenName,
                MaxTraffic = createParams.MaxTraffic,
                MaxClient = createParams.MaxClient,
                EndTime = createParams.EndTime,
                Lifetime = createParams.Lifetime,
                Url = createParams.Url,
                IsPublic = createParams.IsPublic,
                Secret = createParams.Secret ?? aes.Key,
                SupportCode = supportCode,
            };

            await vhContext.AccessTokens.AddAsync(accessToken);
            await vhContext.SaveChangesAsync();
            return accessToken;
        }

        [HttpPut("{accessTokenId}")]
        public async Task<AccessToken> Update(Guid projectId, Guid accessTokenId, AccessTokenUpdateParams updateParams)
        {
            await using VhContext vhContext = new();

            // validate accessToken.AccessTokenGroupId
            if (updateParams.AccessTokenGroupId != null)
                await vhContext.AccessTokenGroups.SingleAsync(x => x.ProjectId == projectId && x.AccessTokenGroupId == updateParams.AccessTokenGroupId);

            // update
            var accessToken = await vhContext.AccessTokens.SingleAsync(x => x.ProjectId == projectId && x.AccessTokenId == accessTokenId);
            if (updateParams.AccessTokenGroupId != null) accessToken.AccessTokenGroupId = updateParams.AccessTokenGroupId;
            if (updateParams.AccessTokenName != null) accessToken.AccessTokenName = updateParams.AccessTokenName;
            if (updateParams.EndTime != null) accessToken.EndTime = updateParams.EndTime;
            if (updateParams.Lifetime != null) accessToken.Lifetime = updateParams.Lifetime;
            if (updateParams.MaxClient != null) accessToken.MaxClient = updateParams.MaxClient;
            if (updateParams.MaxTraffic != null) accessToken.MaxTraffic = updateParams.MaxTraffic;
            if (updateParams.Url != null) accessToken.Url = updateParams.Url;
            vhContext.AccessTokens.Update(accessToken);

            await vhContext.SaveChangesAsync();
            return accessToken;
        }


        [HttpGet("{accessTokenId}/access-key")]
        public async Task<AccessKey> GetAccessKey(Guid projectId, Guid accessTokenId)
        {
            // get accessToken with default endPoint
            await using VhContext vhContext = new();

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
            var token = new Token(accessToken.Secret, x509Certificate.GetCertHash(), serverEndPoint.CertificateCommonName)
            {
                Version = 1,
                TokenId = accessToken.AccessTokenId,
                Name = accessToken.AccessTokenName,
                SupportId = accessToken.SupportCode,
                HostEndPoint = IPEndPoint.Parse(serverEndPoint.PulicEndPoint),
                HostPort = IPEndPoint.Parse(serverEndPoint.PulicEndPoint).Port,
                IsPublic = accessToken.IsPublic,
                Url = accessToken.Url,
            };

            return new AccessKey(token.ToAccessKey());
        }

        [HttpGet("{accessTokenId}")]
        public async Task<AccessTokenData> Get(Guid projectId, Guid accessTokenId)
        {
            var items = await List(projectId: projectId, accessTokenId: accessTokenId);
            return items.Single();
        }


        [HttpGet("list")]
        public async Task<AccessTokenData[]> List(Guid projectId, Guid? accessTokenId = null, Guid? accessTokenGroupId = null, int recordIndex = 0, int recordCount = 300)
        {
            await using VhContext vhContext = new();
            var query = from AT in vhContext.AccessTokens.Include(x => x.AccessTokenGroup)
                        join AU in vhContext.AccessUsages on new { key1 = AT.AccessTokenId, key2 = AT.IsPublic } equals new { key1 = AU.AccessTokenId, key2 = false } into grouping
                        from AU in grouping.DefaultIfEmpty()
                        where AT.ProjectId == projectId && AT.AccessTokenGroup != null
                        select new AccessTokenData
                        {
                            AccessToken = AT,
                            AccessUsage = AU
                        };

            if (accessTokenId != null)
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
            await using VhContext vhContext = new();
            return await vhContext.AccessUsages
                .Include(x => x.Client)
                .Include(x => x.AccessToken)
                .Where(x => x.AccessToken!.ProjectId == projectId &&
                        x.AccessToken.AccessTokenId == accessTokenId &&
                        (!x.AccessToken.IsPublic || x.Client!.ClientId == clientId))
                .SingleOrDefaultAsync();
        }

        [HttpGet("{accessTokenId}/usage-logs")]
        public async Task<AccessUsageLog[]> GetAccessUsageLogs(Guid projectId, Guid? accessTokenId = null, Guid? clientId = null, int recordIndex = 0, int recordCount = 1000)
        {
            await using VhContext vhContext = new();
            var query = vhContext.AccessUsageLogs
                .Include(x => x.Server)
                .Include(x => x.Session)
                .Include(x => x.Session!.AccessUsage)
                .Include(x => x.Session!.Client)
                .Include(x => x.Session!.AccessUsage!.AccessToken)
                .Where(x => x.Session!.Client!.ProjectId == projectId &&
                            x.Server != null && x.Session.Client != null && x.Session != null && x.Session.AccessUsage != null && x.Session.AccessUsage.AccessToken != null);

            if (accessTokenId != null)
                query = query
                    .Where(x => x.Session!.AccessUsage!.AccessTokenId == accessTokenId);

            if (clientId != null)
                query = query
                    .Where(x => x.Session!.Client!.ClientId == clientId);

            var res = await query
                .OrderByDescending(x => x.AccessUsageLogId)
                .Skip(recordIndex).Take(recordCount)
                .ToArrayAsync();

            return res;
        }
    }
}

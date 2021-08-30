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
                createParams.AccessTokenGroupId =
                    (await vhContext.AccessTokenGroups.SingleAsync(x => x.ProjectId == projectId && x.IsDefault))
                    .AccessTokenGroupId;
            else
                await vhContext.AccessTokenGroups.SingleAsync(x =>
                    x.ProjectId == projectId && x.AccessTokenGroupId == createParams.AccessTokenGroupId);

            // create support id
            var supportCode = await vhContext.AccessTokens.Where(x => x.ProjectId == projectId)
                .MaxAsync(x => (int?) x.SupportCode) ?? 1000;
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
                SupportCode = supportCode
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
                await vhContext.AccessTokenGroups.SingleAsync(x =>
                    x.ProjectId == projectId && x.AccessTokenGroupId == updateParams.AccessTokenGroupId);

            // update
            var accessToken =
                await vhContext.AccessTokens.SingleAsync(x =>
                    x.ProjectId == projectId && x.AccessTokenId == accessTokenId);
            if (updateParams.AccessTokenGroupId != null)
                accessToken.AccessTokenGroupId = updateParams.AccessTokenGroupId;
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


        [HttpGet("{accessTokenId:guid}/access-key")]
        public async Task<AccessTokenKey> GetAccessKey(Guid projectId, Guid accessTokenId)
        {
            // get accessToken with default endPoint
            await using VhContext vhContext = new();

            var query = from ac in vhContext.Projects
                join atg in vhContext.AccessTokenGroups on ac.ProjectId equals atg.ProjectId
                join at in vhContext.AccessTokens on atg.AccessTokenGroupId equals at.AccessTokenGroupId
                join ep in vhContext.ServerEndPoints on atg.AccessTokenGroupId equals ep.AccessTokenGroupId
                where ac.ProjectId == projectId && at.AccessTokenId == accessTokenId && ep.IsDefault
                select new {at, ep};
            var result = await query.SingleAsync();

            var accessToken = result.at;
            var serverEndPoint = result.ep;
            var x509Certificate = new X509Certificate2(serverEndPoint.CertificateRawData);

            // create token
            var token = new Token(accessToken.Secret, x509Certificate.GetCertHash(),
                serverEndPoint.CertificateCommonName)
            {
                Version = 1,
                TokenId = accessToken.AccessTokenId,
                Name = accessToken.AccessTokenName,
                SupportId = accessToken.SupportCode,
                HostEndPoint = IPEndPoint.Parse(serverEndPoint.PublicEndPoint),
                HostPort = IPEndPoint.Parse(serverEndPoint.PublicEndPoint).Port,
                IsPublic = accessToken.IsPublic,
                Url = accessToken.Url
            };

            return new AccessTokenKey(token.ToAccessKey());
        }

        [HttpGet("{accessTokenId:guid}")]
        public async Task<AccessTokenData> Get(Guid projectId, Guid accessTokenId)
        {
            var items = await ListInternal(projectId, accessTokenId);
            return items.Single();
        }


        [HttpGet("list")]
        public Task<AccessTokenData[]> List(Guid projectId, Guid? accessTokenGroupId = null,
            int recordIndex = 0, int recordCount = 1000)
        {
            return ListInternal(projectId, null, accessTokenGroupId, recordIndex, recordCount);
        }

        private static async Task<AccessTokenData[]> ListInternal(Guid projectId, Guid? accessTokenId = null, Guid? accessTokenGroupId = null, 
            int recordIndex = 0, int recordCount = 300)
        {
            await using VhContext vhContext = new();
            var query = from at in vhContext.AccessTokens.Include(x => x.AccessTokenGroup)
                join au in vhContext.Accesses on new {key1 = at.AccessTokenId, key2 = at.IsPublic} equals new
                    {key1 = au.AccessTokenId, key2 = false} into grouping
                from au in grouping.DefaultIfEmpty()
                where at.ProjectId == projectId && at.AccessTokenGroup != null
                select new AccessTokenData
                {
                    AccessToken = at,
                    Access = au
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

        [HttpGet("{accessTokenId:guid}/usage")]
        public async Task<Access> GetAccess(Guid projectId, Guid accessTokenId, Guid? clientId = null)
        {
            await using VhContext vhContext = new();
            return await vhContext.Accesses
                .Include(x => x.ProjectClient)
                .Include(x => x.AccessToken)
                .Where(x => x.AccessToken!.ProjectId == projectId &&
                            x.AccessToken.AccessTokenId == accessTokenId &&
                            (!x.AccessToken.IsPublic || x.ProjectClient!.ClientId == clientId))
                .SingleOrDefaultAsync();
        }

        [HttpGet("{accessTokenId:guid}/usage-logs")]
        public async Task<AccessLog[]> GetAccessLogs(Guid projectId, Guid? accessTokenId = null,
            Guid? clientId = null, int recordIndex = 0, int recordCount = 1000)
        {
            await using VhContext vhContext = new();
            var query = vhContext.AccessLogs
                .Include(x => x.Server)
                .Include(x => x.Session)
                .Include(x => x.Session!.Access)
                .Include(x => x.Session!.Client)
                .Include(x => x.Session!.Access!.AccessToken)
                .Where(x => x.Session!.Client!.ProjectId == projectId &&
                            x.Server != null && x.Session.Client != null && x.Session != null &&
                            x.Session.Access != null && x.Session.Access.AccessToken != null);

            if (accessTokenId != null)
                query = query
                    .Where(x => x.Session!.Access!.AccessTokenId == accessTokenId);

            if (clientId != null)
                query = query
                    .Where(x => x.Session!.Client!.ClientId == clientId);

            var res = await query
                .OrderByDescending(x => x.AccessLogId)
                .Skip(recordIndex).Take(recordCount)
                .ToArrayAsync();

            return res;
        }
    }
}
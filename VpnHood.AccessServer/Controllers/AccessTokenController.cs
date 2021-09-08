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
            if (createParams.AccessPointGroupId == null)
                createParams.AccessPointGroupId =
                    (await vhContext.AccessPointGroups.SingleAsync(x => x.ProjectId == projectId && x.IsDefault))
                    .AccessPointGroupId;
            else
                await vhContext.AccessPointGroups.SingleAsync(x =>
                    x.ProjectId == projectId && x.AccessPointGroupId == createParams.AccessPointGroupId);

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
                AccessPointGroupId = createParams.AccessPointGroupId.Value,
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

            // validate accessToken.AccessPointGroupId
            if (updateParams.AccessPointGroupId != null)
                await vhContext.AccessPointGroups.SingleAsync(x =>
                    x.ProjectId == projectId && x.AccessPointGroupId == updateParams.AccessPointGroupId);

            // update
            var accessToken =
                await vhContext.AccessTokens.SingleAsync(x =>
                    x.ProjectId == projectId && x.AccessTokenId == accessTokenId);
            if (updateParams.AccessPointGroupId != null)
                accessToken.AccessPointGroupId = updateParams.AccessPointGroupId;
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
            // get accessToken with default accessPoint
            await using VhContext vhContext = new();

            var query = from ac in vhContext.Projects
                join atg in vhContext.AccessPointGroups on ac.ProjectId equals atg.ProjectId
                join c in vhContext.Certificates on atg.CertificateId equals c.CertificateId
                join at in vhContext.AccessTokens on atg.AccessPointGroupId equals at.AccessPointGroupId
                join ep in vhContext.AccessPoints on atg.AccessPointGroupId equals ep.AccessPointGroupId
                where ac.ProjectId == projectId && at.AccessTokenId == accessTokenId && ep.IsDefault
                select new {at, ep, c};
            var result = await query.SingleAsync();

            var accessToken = result.at;
            var accessPoint = result.ep;
            var certificate = result.c;
            var x509Certificate = new X509Certificate2(certificate.RawData);

            // create token
            var token = new Token(accessToken.Secret, x509Certificate.GetCertHash(),
                certificate.CommonName)
            {
                Version = 1,
                TokenId = accessToken.AccessTokenId,
                Name = accessToken.AccessTokenName,
                SupportId = accessToken.SupportCode,
                HostEndPoint = IPEndPoint.Parse(accessPoint.PublicEndPoint),
                HostPort = IPEndPoint.Parse(accessPoint.PublicEndPoint).Port,
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


        [HttpGet]
        public Task<AccessTokenData[]> List(Guid projectId, Guid? accessPointGroupId = null,
            int recordIndex = 0, int recordCount = 1000)
        {
            return ListInternal(projectId, null, accessPointGroupId, recordIndex, recordCount);
        }

        private static async Task<AccessTokenData[]> ListInternal(Guid projectId, Guid? accessTokenId = null, Guid? accessPointGroupId = null, 
            int recordIndex = 0, int recordCount = 300)
        {
            await using VhContext vhContext = new();
            var query = from at in vhContext.AccessTokens.Include(x => x.AccessPointGroup)
                join au in vhContext.Accesses on new {key1 = at.AccessTokenId, key2 = at.IsPublic} equals new
                    {key1 = au.AccessTokenId, key2 = false} into grouping
                from au in grouping.DefaultIfEmpty()
                where at.ProjectId == projectId && at.AccessPointGroup != null
                select new AccessTokenData
                {
                    AccessToken = at,
                    Access = au
                };

            if (accessTokenId != null)
                query = query.Where(x => x.AccessToken.AccessTokenId == accessTokenId);

            if (accessPointGroupId != null)
                query = query.Where(x => x.AccessToken.AccessPointGroupId == accessPointGroupId);

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
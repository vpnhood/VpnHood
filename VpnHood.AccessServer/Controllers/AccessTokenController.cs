using System;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.DTOs;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Security;
using VpnHood.Common;

namespace VpnHood.AccessServer.Controllers
{
    [Route("/api/projects/{projectId:guid}/access-tokens")]
    public class AccessTokenController : SuperController<AccessTokenController>
    {
        public AccessTokenController(ILogger<AccessTokenController> logger) : base(logger)
        {
        }

        [HttpPost]
        public async Task<AccessToken> Create(Guid projectId, AccessTokenCreateParams createParams)
        {
            // find default serveEndPoint 
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.AccessTokenWrite);

            var accessPointGroup = await vhContext.AccessPointGroups
                .SingleAsync(x => x.ProjectId == projectId && x.AccessPointGroupId == createParams.AccessPointGroupId);

            // create support id
            var supportCode = await vhContext.AccessTokens.Where(x => x.ProjectId == projectId)
                .MaxAsync(x => (int?)x.SupportCode) ?? 1000;
            supportCode++;

            AccessToken accessToken = new()
            {
                AccessTokenId = createParams.AccessTokenId ?? Guid.NewGuid(),
                ProjectId = projectId,
                AccessPointGroupId = accessPointGroup.AccessPointGroupId,
                AccessTokenName = createParams.AccessTokenName,
                MaxTraffic = createParams.MaxTraffic,
                MaxClient = createParams.MaxClient,
                EndTime = createParams.EndTime,
                Lifetime = createParams.Lifetime,
                Url = createParams.Url,
                IsPublic = createParams.IsPublic,
                Secret = createParams.Secret ?? Util.GenerateSessionKey(),
                SupportCode = supportCode
            };

            await vhContext.AccessTokens.AddAsync(accessToken);
            await vhContext.SaveChangesAsync();
            return accessToken;
        }

        [HttpPatch("{accessTokenId:guid}")]
        public async Task<AccessToken> Update(Guid projectId, Guid accessTokenId, AccessTokenUpdateParams updateParams)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.AccessTokenWrite);

            // validate accessToken.AccessPointGroupId
            if (updateParams.AccessPointGroupId != null)
                await vhContext.AccessPointGroups.SingleAsync(x =>
                    x.ProjectId == projectId && x.AccessPointGroupId == updateParams.AccessPointGroupId);

            // update
            var accessToken = await vhContext.AccessTokens.SingleAsync(x => x.ProjectId == projectId && x.AccessTokenId == accessTokenId);
            if (updateParams.AccessPointGroupId != null) accessToken.AccessPointGroupId = updateParams.AccessPointGroupId;
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
        [Produces(MediaTypeNames.Text.Plain)]
        public async Task<string> GetAccessKey(Guid projectId, Guid accessTokenId)
        {
            // get accessToken with default accessPoint
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.AccessTokenReadAccessKey);

            var accessToken = await vhContext
                .AccessTokens
                .Include(x => x.AccessPointGroup)
                .Include(x => x.AccessPointGroup!.Certificate)
                .Include(x => x.AccessPointGroup!.AccessPoints)
                .Where(x => x.ProjectId == projectId && x.AccessTokenId == accessTokenId)
                .SingleAsync();

            if (Util.IsNullOrEmpty(accessToken.AccessPointGroup?.AccessPoints?.ToArray()))
                throw new InvalidOperationException($"Could not find any access point for the {nameof(AccessPointGroup)}!");

            //var accessToken = result.at;
            var certificate = accessToken.AccessPointGroup.Certificate!;
            var x509Certificate = new X509Certificate2(certificate.RawData);
            var accessPoints = accessToken.AccessPointGroup.AccessPoints
                .Where(x => x.AccessPointMode == AccessPointMode.PublicInToken)
                .ToArray();

            // create token
            var token = new Token(accessToken.Secret, x509Certificate.GetCertHash(), certificate.CommonName)
            {
                Version = 1,
                TokenId = accessToken.AccessTokenId,
                Name = accessToken.AccessTokenName,
                SupportId = accessToken.SupportCode,
                HostEndPoints = accessPoints.Select(x => new IPEndPoint(IPAddress.Parse(x.IpAddress), x.TcpPort)).ToArray(),
                HostPort = 0, //valid hostname is not supported yet
                IsValidHostName = false,
                IsPublic = accessToken.IsPublic,
                Url = accessToken.Url
            };

            return token.ToAccessKey();
        }

        [HttpGet("{accessTokenId:guid}")]
        public async Task<AccessTokenData> Get(Guid projectId, Guid accessTokenId)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.AccessTokenRead);

            var items = await ListInternal(vhContext, projectId, accessTokenId);
            return items.Single();
        }


        [HttpGet]
        public async Task<AccessTokenData[]> List(Guid projectId, Guid? accessPointGroupId = null,
            int recordIndex = 0, int recordCount = 1000)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.AccessTokenRead);
            var ret = await ListInternal(vhContext, projectId, null, accessPointGroupId, recordIndex, recordCount);
            return ret;
        }

        private static async Task<AccessTokenData[]> ListInternal(VhContext vhContext, Guid projectId, Guid? accessTokenId = null, Guid? accessPointGroupId = null,
            int recordIndex = 0, int recordCount = 300)
        {
            var query = from accessToken in vhContext.AccessTokens.Include(x => x.AccessPointGroup)
                        join access in vhContext.Accesses on new { key1 = accessToken.AccessTokenId, key2 = accessToken.IsPublic } equals new
                        { key1 = access.AccessTokenId, key2 = false } into grouping
                        from access in grouping.DefaultIfEmpty()
                        where accessToken.ProjectId == projectId && accessToken.AccessPointGroup != null
                        select new AccessTokenData
                        {
                            AccessToken = accessToken,
                            Access = access
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
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.AccessTokenRead);

            return await vhContext.Accesses
                .Include(x => x.ProjectClient)
                .Include(x => x.AccessToken)
                .Where(x => x.AccessToken!.ProjectId == projectId &&
                            x.AccessToken.AccessTokenId == accessTokenId &&
                            (!x.AccessToken.IsPublic || x.ProjectClient!.ClientId == clientId))
                .SingleOrDefaultAsync();
        }

        [HttpGet("{accessTokenId:guid}/usage-logs")]
        public async Task<AccessUsage[]> GetAccessLogs(Guid projectId, Guid? accessTokenId = null,
            Guid? clientId = null, int recordIndex = 0, int recordCount = 1000)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.AccessTokenRead);

            var query = vhContext.AccessUsages
                .Include(x => x.Server)
                .Include(x => x.Session)
                .Include(x => x.Session!.Access)
                .Include(x => x.Session!.Client)
                .Include(x => x.Session!.Access!.AccessToken)
                .Where(x => x.Session!.Client!.ProjectId == projectId &&
                            x.Server != null &&
                            x.Session.Client != null &&
                            x.Session != null &&
                            x.Session.Access != null &&
                            x.Session.Access.AccessToken != null);

            if (accessTokenId != null)
                query = query
                    .Where(x => x.Session!.Access!.AccessTokenId == accessTokenId);

            if (clientId != null)
                query = query
                    .Where(x => x.Session!.Client!.ClientId == clientId);

            var res = await query
                .OrderByDescending(x => x.AccessUsageId)
                .Skip(recordIndex).Take(recordCount)
                .ToArrayAsync();

            return res;
        }

        [HttpDelete("{accessTokenId:guid}")]
        public async Task Delete(Guid projectId, Guid accessTokenId)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.AccessTokenWrite);

            var accessToken = await vhContext.AccessTokens
                .SingleAsync(x => x.ProjectId == projectId && x.AccessTokenId == accessTokenId);

            vhContext.AccessTokens.Remove(accessToken);
            await vhContext.SaveChangesAsync();
        }
    }
}
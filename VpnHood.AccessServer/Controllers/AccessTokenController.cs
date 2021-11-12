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
    [Route("/bpi/projects/{projectId:guid}/access-tokens")]
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
            DateTime? startTime = null, DateTime? endTime = null, int recordIndex = 0, int recordCount = 1000)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.AccessTokenRead);
            var ret = await ListInternal(vhContext, projectId, null, accessPointGroupId, startTime, endTime, recordIndex, recordCount);
            return ret;
        }

        private static async Task<AccessTokenData[]> ListInternal(VhContext vhContext, Guid projectId, Guid? accessTokenId = null, Guid? accessPointGroupId = null,
            DateTime? starTime = null, DateTime? endTime = null, int recordIndex = 0, int recordCount = 300)
        {
            var hasStartTime = starTime != null;
            var hasEndTime = endTime != null && endTime < DateTime.UtcNow.AddHours(-1);

            // calculate usage
            var query1 =
                from accessToken in vhContext.AccessTokens
                join session in vhContext.Sessions on accessToken.AccessTokenId equals session.AccessTokenId into grouping2
                from session in grouping2.DefaultIfEmpty()
                join accessUsage in vhContext.AccessUsages on new { key1 = session.SessionId } equals new { key1 = accessUsage.SessionId } into grouping3
                from accessUsage in grouping3.DefaultIfEmpty()
                where accessToken.ProjectId == projectId &&
                        (accessTokenId == null || accessToken.AccessTokenId == accessTokenId) &&
                        (accessPointGroupId == null || accessToken.AccessPointGroupId == accessPointGroupId) &&
                        (starTime == null || accessUsage.CreatedTime >= starTime) &&
                        (endTime == null || accessUsage.CreatedTime <= endTime)
                group new { session, accessUsage } by accessToken.AccessTokenId into g
                select new
                {
                    AccessTokenId = g.Key,
                    Usage = new Usage
                    {
                        LastTime = g.Max(x => x.accessUsage.CreatedTime),
                        SentTraffic = g.Sum(x => x.accessUsage.SentTraffic),
                        ReceivedTraffic = g.Sum(x => x.accessUsage.ReceivedTraffic),
                        ServerCount = g.Select(x => x.session.ServerId).Distinct().Count(),
                        ClientCount = g.Select(x => x.session.ProjectClientId).Distinct().Count(),
                    }
                };

            // filter
            query1 = query1
                .Skip(recordIndex)
                .Take(recordCount);

            // create output
            var query2 = from accessToken in vhContext.AccessTokens.Include(x => x.AccessPointGroup)
                         join usage in query1 on accessToken.AccessTokenId equals usage.AccessTokenId
                         select new AccessTokenData
                         {
                             AccessToken = accessToken,
                             Usage = usage.Usage
                         };

            var res = await query2.ToArrayAsync();
            return res;
        }


        [HttpGet("{accessTokenId:guid}/usage-logs")]
        public async Task<AccessUsageEx[]> GetAccessUsages(Guid projectId, Guid? accessTokenId = null,
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

        [HttpGet("{accessTokenId:guid}/usage")]
        public Task<AccessUsageEx> GetAccessUsage(Guid projectId, Guid tokenId, Guid clientId)
        {
            throw new NotImplementedException();
        }
    }
}
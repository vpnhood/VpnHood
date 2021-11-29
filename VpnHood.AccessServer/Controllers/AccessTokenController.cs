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
using VpnHood.AccessServer.Exceptions;
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

            // check user quota
            using var singleRequest = SingleRequest.Start($"CreateAccessTokens_{CurrentUserId}");
            if (vhContext.AccessTokens.Count(x => x.ProjectId == projectId) >= QuotaConstants.AccessTokenCount)
                throw new QuotaException(nameof(VhContext.AccessTokens), QuotaConstants.AccessTokenCount);

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
                MaxDevice = createParams.MaxDevice,
                EndTime = createParams.EndTime,
                Lifetime = createParams.Lifetime,
                Url = createParams.Url,
                IsPublic = createParams.IsPublic,
                Secret = createParams.Secret ?? Util.GenerateSessionKey(),
                SupportCode = supportCode,
                CreatedTime = DateTime.UtcNow
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
            if (updateParams.MaxDevice != null) accessToken.MaxDevice = updateParams.MaxDevice;
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

        [HttpGet("{accessTokenId:guid}/usage")]
        public async Task<AccessTokenData> GetUsage(Guid projectId, Guid accessTokenId, Guid? deviceId = null, DateTime? startTime = null, DateTime? endTime = null)
        {
            var items = await GetUsages(projectId, accessTokenId: accessTokenId, deviceId: deviceId,
                startTime: startTime, endTime: endTime);
            return items.Single();
        }

        [HttpGet("usages")]
        public async Task<AccessTokenData[]> GetUsages(Guid projectId,
            Guid? accessTokenId = null, Guid? accessPointGroupId = null, Guid? deviceId = null,
            DateTime? startTime = null, DateTime? endTime = null, int recordIndex = 0, int recordCount = 1000)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.AccessTokenRead);

            // select and order
            var usages =
                from accessUsage in vhContext.AccessUsages
                join session in vhContext.Sessions on accessUsage.SessionId equals session.SessionId
                join accessToken in vhContext.AccessTokens on session.AccessTokenId equals accessToken.AccessTokenId
                where
                    (accessToken.ProjectId == projectId) &&
                    (accessTokenId == null || accessToken.AccessTokenId == accessTokenId) &&
                    (accessPointGroupId == null || accessToken.AccessPointGroupId == accessPointGroupId) &&
                    (deviceId == null || session.DeviceId == deviceId) &&
                    (startTime == null || accessUsage.CreatedTime >= startTime) &&
                    (endTime == null || accessUsage.CreatedTime <= endTime)
                group new { accessUsage, session } by (Guid?)session.AccessTokenId into g
                select new
                {
                    GroupByKeyId = g.Key,
                    LastAccessUsageId = g.Key != null ? (long?)g.Select(x => x.accessUsage.AccessUsageId).Max() : null,
                    Usage = g.Key != null ? new Usage
                    {
                        LastTime = g.Max(y => y.accessUsage.CreatedTime),
                        AccessCount = g.Select(y => y.accessUsage.AccessId).Distinct().Count(),
                        SessionCount = g.Select(y => y.session.SessionId).Distinct().Count(),
                        ServerCount = g.Select(y => y.session.ServerId).Distinct().Count(),
                        DeviceCount = g.Select(y => y.session.DeviceId).Distinct().Count(),
                        AccessTokenCount = g.Select(y => y.session.AccessTokenId).Distinct().Count(),
                        SentTraffic = g.Sum(y => y.accessUsage.SentTraffic),
                        ReceivedTraffic = g.Sum(y => y.accessUsage.ReceivedTraffic)
                    } : null
                };

            // create output
            var query =
                    from accessToken in vhContext.AccessTokens.Include(x => x.AccessPointGroup)
                    join usage in usages on accessToken.AccessTokenId equals usage.GroupByKeyId into usageGrouping
                    from usage in usageGrouping.DefaultIfEmpty()
                    join accessUsage in vhContext.AccessUsages on usage.LastAccessUsageId equals accessUsage.AccessUsageId into accessUsageGrouping
                    from accessUsage in accessUsageGrouping.DefaultIfEmpty()
                    where
                       (accessToken.ProjectId == projectId) &&
                       (accessTokenId == null || accessToken.AccessTokenId == accessTokenId) &&
                       (accessPointGroupId == null || accessToken.AccessPointGroupId == accessPointGroupId)
                    select new AccessTokenData
                    {
                        AccessToken = accessToken,
                        Usage = usage.Usage,
                        LastAccessUsage = accessUsage,
                    };

            query = query
                .Skip(recordIndex)
                .Take(recordCount);

            var res = await query.ToArrayAsync();
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
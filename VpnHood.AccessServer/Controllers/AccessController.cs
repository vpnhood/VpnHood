using System;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Models;
using VpnHood.Server;
using System.Collections.Generic;
using VpnHood.AccessServer.Exceptions;

//todo use nuget

namespace VpnHood.AccessServer.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    [Authorize(AuthenticationSchemes = "auth", Roles = "Admin, VpnServer")]
    public class AccessController : SuperController<AccessController>
    {
        protected Guid ProjectId
        {
            get
            {
                var res = User.Claims.FirstOrDefault(claim => claim.Type == "project_id")?.Value ?? throw new UnauthorizedAccessException();
                return Guid.Parse(res);
            }
        }

        public AccessController(ILogger<AccessController> logger) : base(logger)
        {
        }

        [HttpPost]
        public async Task<Access> AddUsage(Guid serverId, UsageParams usageParams)
        {
            if (usageParams.ClientIdentity == null) throw new ArgumentException($"{nameof(usageParams.ClientIdentity)} should not be null", nameof(usageParams));
            if (usageParams.AccessId == null) throw new ArgumentException($"{nameof(usageParams.AccessId)} should not be null", nameof(usageParams));
            var clientIdentity = usageParams.ClientIdentity;

            _logger.LogInformation($"AddUsage for {clientIdentity}, SentTraffic: {usageParams.SentTrafficByteCount / 1000000} MB, ReceivedTraffic: {usageParams.ReceivedTrafficByteCount / 1000000} MB");

            // update accessUsage
            using VhContext vhContext = new();
            var res = await (
                from AU in vhContext.AccessUsages
                join AT in vhContext.AccessTokens on AU.AccessTokenId equals AT.AccessTokenId
                join C in vhContext.Clients on AT.ProjectId equals C.ProjectId
                where AT.ProjectId == ProjectId && AU.AccessUsageId == Guid.Parse(usageParams.AccessId) && C.ClientId == usageParams.ClientIdentity.ClientId
                select new { AU, AT, C }
                ).SingleAsync();

            var accessUsage = res.AU;
            accessUsage.CycleReceivedTraffic += usageParams.ReceivedTrafficByteCount;
            accessUsage.CycleSentTraffic += usageParams.SentTrafficByteCount;
            accessUsage.TotalReceivedTraffic += usageParams.ReceivedTrafficByteCount;
            accessUsage.TotalSentTraffic += usageParams.SentTrafficByteCount;
            accessUsage.ModifiedTime = DateTime.Now;
            vhContext.AccessUsages.Update(accessUsage);

            var ret = await CreateAccess(vhContext, serverId, accessToken: res.AT, accessUsage: res.AU, client: res.C, usageParams: usageParams);
            await vhContext.SaveChangesAsync();
            return ret;
        }

        private static async Task<Access> CreateAccess(VhContext vhContext, Guid serverId,
            AccessToken accessToken, AccessUsage accessUsage, Client client, UsageParams usageParams)
        {
            // insert AccessUsageLog
            await vhContext.AccessUsageLogs.AddAsync(new()
            {
                AccessUsageId = accessUsage.AccessUsageId,
                ClientKeyId = client.ClientKeyId,
                ClientIp = usageParams.ClientIdentity.ClientIp?.ToString(),
                ClientVersion = usageParams.ClientIdentity.ClientVersion,
                ReceivedTraffic = usageParams.ReceivedTrafficByteCount,
                SentTraffic = usageParams.SentTrafficByteCount,
                CycleReceivedTraffic = accessUsage.CycleReceivedTraffic,
                CycleSentTraffic = accessUsage.CycleSentTraffic,
                TotalReceivedTraffic = accessUsage.TotalReceivedTraffic,
                TotalSentTraffic = accessUsage.TotalSentTraffic,
                ServerId = serverId,
                CreatedTime = DateTime.Now
            });

            // create access
            var access = new Access()
            {
                AccessId = usageParams.AccessId,
                Secret = accessToken.Secret,
                ExpirationTime = accessToken.EndTime,
                MaxClientCount = accessToken.MaxClient,
                MaxTrafficByteCount = accessToken.MaxTraffic,
                ReceivedTrafficByteCount = accessUsage.CycleReceivedTraffic,
                SentTrafficByteCount = accessUsage.CycleSentTraffic,
            };

            // calculate status
            if (access.ExpirationTime < DateTime.Now)
                access.StatusCode = AccessStatusCode.Expired;
            else if (accessToken.MaxTraffic != 0 && access.SentTrafficByteCount + access.ReceivedTrafficByteCount > accessToken.MaxTraffic)
                access.StatusCode = AccessStatusCode.TrafficOverflow;
            else
                access.StatusCode = AccessStatusCode.Ok;

            return access;
        }

        [HttpGet]
        public async Task<Access> GetAccess(Guid serverId, AccessParams accessParams)
        {
            var clientIdentity = accessParams.ClientIdentity ?? throw new ArgumentException($"{nameof(AccessParams.ClientIdentity)} should not be null.", nameof(accessParams));
            _logger.LogInformation($"Getting Access. TokenId: {clientIdentity.TokenId}, ClientId: {clientIdentity.ClientId}");

            using VhContext vhContext = new();

            // check projectId, accessToken, accessTokenGroup
            var query = from ATG in vhContext.AccessTokenGroups
                        join AT in vhContext.AccessTokens on ATG.AccessTokenGroupId equals AT.AccessTokenGroupId
                        join EP in vhContext.ServerEndPoints on ATG.AccessTokenGroupId equals EP.AccessTokenGroupId
                        where ATG.ProjectId == ProjectId && AT.AccessTokenId == clientIdentity.TokenId &&
                                (EP.PulicEndPoint == accessParams.RequestEndPoint.ToString() || EP.PrivateEndPoint == accessParams.RequestEndPoint.ToString())
                        select new { AT, EP.ServerId, EP.ServerEndPointId };
            var result = await query.SingleAsync();
            var accessToken = result.AT;

            // update client
            var client = await vhContext.Clients.SingleOrDefaultAsync(x => x.ProjectId == ProjectId && x.ClientId == clientIdentity.ClientId);
            if (client == null)
            {
                client = new Client
                {
                    ClientKeyId = Guid.NewGuid(),
                    ProjectId = ProjectId,
                    ClientId = clientIdentity.ClientId,
                    ClientVersion = clientIdentity.ClientVersion,
                    UserAgent = clientIdentity.UserAgent,
                    CreatedTime = DateTime.Now,
                };
                await vhContext.Clients.AddAsync(client);
            }
            else
            {
                client.UserAgent = clientIdentity.UserAgent;
                client.ClientVersion = clientIdentity.ClientVersion;
                vhContext.Clients.Update(client);
            }

            // update ServerEndPoint.ServerId
            if (result.ServerId != serverId)
            {
                var serverEndPoint = new ServerEndPoint() { ServerEndPointId = result.ServerEndPointId };
                vhContext.Entry(serverEndPoint).State = EntityState.Unchanged;
                vhContext.Entry(serverEndPoint).Property(x => x.ServerId).IsModified = true;
            }

            // set expiration time on first use
            if (accessToken.EndTime == null && accessToken.Lifetime != 0)
            {
                accessToken.EndTime = DateTime.Now.AddDays(accessToken.Lifetime);
                _logger.LogInformation($"Access has been activated! Expiration: {accessToken.EndTime}, ClientIdentity: {accessParams.ClientIdentity}");
            }

            // get or create accessUsage
            Guid? clientKeyId = accessToken.IsPublic ? client.ClientKeyId : null;
            var accessUsage = await vhContext.AccessUsages.SingleOrDefaultAsync(x => x.AccessTokenId == accessToken.AccessTokenId && x.ClientKeyId == clientKeyId);
            if (accessUsage == null)
            {
                accessUsage = new AccessUsage
                {
                    AccessUsageId = Guid.NewGuid(),
                    AccessTokenId = clientIdentity.TokenId,
                    ClientKeyId = accessToken.IsPublic ? client.ClientKeyId : null,
                    ConnectTime = DateTime.Now,
                    ModifiedTime = DateTime.Now
                };
                await vhContext.AccessUsages.AddAsync(accessUsage);
            }
            else
            {
                accessUsage.ModifiedTime = DateTime.Now;
                accessUsage.ConnectTime = DateTime.Now;
                vhContext.AccessUsages.Update(accessUsage);
            }

            var ret = await CreateAccess(vhContext, serverId,
                accessToken: accessToken,
                accessUsage: accessUsage,
                usageParams: new UsageParams
                {
                    AccessId = accessUsage.AccessUsageId.ToString(),
                    ClientIdentity = clientIdentity
                },
                client: client);

            await vhContext.SaveChangesAsync();
            return ret;
        }

        [HttpGet]
        public async Task<byte[]> GetSslCertificateData(Guid serverId, string requestEndPoint)
        {
            using VhContext vhContext = new();
            var serverEndPoint = await vhContext.ServerEndPoints.SingleAsync(x => x.ProjectId == ProjectId && (x.PulicEndPoint == requestEndPoint || x.PrivateEndPoint == requestEndPoint));

            // update serverId associated with ServerEndPoint
            if (serverEndPoint.ServerId != serverId)
            {
                serverEndPoint.ServerId = serverId;
                vhContext.ServerEndPoints.Update(serverEndPoint);
                vhContext.Entry(serverEndPoint).Property(x => x.CertificateRawData).IsModified = false;
                await vhContext.SaveChangesAsync();
            }

            return serverEndPoint.CertificateRawData;
        }

        [HttpPost]
        public async Task SendServerStatus(Guid serverId, ServerStatus serverStatus)
        {
            // get current accessToken
            await PublicCycleHelper.UpdateCycle();

            using VhContext vhContext = new();
            var query = from S in vhContext.Servers
                        join SSL in vhContext.ServerStatusLogs on S.ServerId equals SSL.ServerId into grouping
                        from SSL in grouping.DefaultIfEmpty()
                        where S.ServerId == serverId && SSL.IsLast
                        select new { S, SSL };
            var queryRes = await query.SingleAsync();

            // remove IsLast
            if (queryRes.SSL != null)
            {
                queryRes.SSL.IsLast = false;
                vhContext.Update(queryRes.SSL);
            }

            vhContext.ServerStatusLogs.Add(new ServerStatusLog()
            {
                ServerId = serverId,
                IsSubscribe = false,
                IsLast = true,
                CreatedTime = DateTime.Now,
                FreeMemory = serverStatus.FreeMemory,
                NatTcpCount = serverStatus.NatTcpCount,
                NatUdpCount = serverStatus.NatUdpCount,
                SessionCount = serverStatus.SessionCount,
                ThreadCount = serverStatus.ThreadCount,
            });
            await vhContext.SaveChangesAsync();
        }

        [HttpPost]
        public async Task Subscribe(Guid serverId, ServerInfo serverInfo)
        {
            using VhContext vhContext = new();
            var server = await vhContext.Servers.FindAsync(serverId);
            var isNew = server == null;
            if (server == null)
            {
                server = new Models.Server()
                {
                    ProjectId = ProjectId,
                    ServerId = serverId,
                    CreatedTime = DateTime.Now
                };
            }
            else if (server.ProjectId != ProjectId)
                throw new AlreadyExistsException($"This serverId is used by another project! Change your server id. id: {serverId}");

            // update server
            server.EnvironmentVersion = serverInfo.EnvironmentVersion?.ToString();
            server.LocalIp = serverInfo.LocalIp;
            server.PublicIp = serverInfo.PublicIp;
            server.OsVersion = serverInfo.OsVersion;
            server.MachineName = serverInfo.MachineName;
            server.SubscribeTime = DateTime.Now;
            server.TotalMemory = serverInfo.TotalMemory;
            server.Version = serverInfo.Version?.ToString();

            // add or update
            if (isNew)
                await vhContext.Servers.AddAsync(server);
            else
                vhContext.Servers.Update(server);

            // remove isLast
            var lastLog = await vhContext.ServerStatusLogs.FirstOrDefaultAsync(e => e.ServerId == serverId && e.IsLast);
            if (lastLog != null)
            {
                lastLog.IsLast = false;
                vhContext.ServerStatusLogs.Update(lastLog);
            }

            // insert new log
            ServerStatusLog statusLog = new()
            {
                ServerId = serverId,
                CreatedTime = DateTime.Now,
                IsLast = true,
                IsSubscribe = true
            };
            await vhContext.ServerStatusLogs.AddAsync(statusLog);
            await vhContext.SaveChangesAsync();
        }
    }
}

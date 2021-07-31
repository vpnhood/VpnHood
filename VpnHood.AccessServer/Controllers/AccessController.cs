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
using System.Transactions;

//todo use nuget

namespace VpnHood.AccessServer.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [Authorize(AuthenticationSchemes = "auth", Roles = "Admin, VpnServer")]
    public class AccessController : SuperController<AccessController>
    {
        protected Guid AccountId
        {
            get
            {
                var res = User.Claims.FirstOrDefault(claim => claim.Type == "account_id")?.Value ?? throw new UnauthorizedAccessException();
                return Guid.Parse(res);
            }
        }

        public AccessController(ILogger<AccessController> logger) : base(logger)
        {
        }

        [HttpPost]
        [Route(nameof(AddUsage))]
        public async Task<Access> AddUsage(Guid serverId, UsageParams usageParams)
        {
            if (usageParams.ClientIdentity == null) throw new ArgumentException($"{nameof(usageParams.ClientIdentity)} should not be null", nameof(usageParams));
            if (usageParams.AccessId == null) throw new ArgumentException($"{nameof(usageParams.AccessId)} should not be null", nameof(usageParams));
            var clientIdentity = usageParams.ClientIdentity;
            _logger.LogInformation($"AddUsage for {clientIdentity}, SentTraffic: {usageParams.SentTrafficByteCount / 1000000} MB, ReceivedTraffic: {usageParams.ReceivedTrafficByteCount / 1000000} MB");

            // get accessToken
            using VhContext vhContext = new();
            var accessToken = await vhContext.AccessTokens.SingleAsync(e => e.AccountId == AccountId && e.AccessTokenId == clientIdentity.TokenId);
            var ret = await AddUsageInternal(vhContext, serverId, accessToken, usageParams, false);
            await vhContext.SaveChangesAsync();
            return ret;
        }

        private async Task<Access> AddUsageInternal(VhContext vhContext, Guid serverId, AccessToken accessToken, UsageParams usageParams, bool isConnecting)
        {
            var clientIdentity = usageParams.ClientIdentity;

            // add usage
            var clientId = accessToken.IsPublic ? clientIdentity.ClientId : Guid.Empty;
            var accessUsage = await vhContext.AccessUsages.FindAsync(clientIdentity.TokenId, clientId);
            if (accessUsage == null)
            {
                accessUsage = new AccessUsage
                {
                    AccessTokenId = clientIdentity.TokenId,
                    ClientId = clientIdentity.ClientId,
                    CycleReceivedTraffic = usageParams.ReceivedTrafficByteCount,
                    CycleSentTraffic = usageParams.SentTrafficByteCount,
                    TotalReceivedTraffic = usageParams.ReceivedTrafficByteCount,
                    TotalSentTraffic = usageParams.SentTrafficByteCount,
                    ConnectTime = DateTime.Now,
                    ModifiedTime = DateTime.Now
                };
                await vhContext.AccessUsages.AddAsync(accessUsage);
            }
            else
            {
                accessUsage.CycleReceivedTraffic += usageParams.ReceivedTrafficByteCount;
                accessUsage.CycleSentTraffic += usageParams.SentTrafficByteCount;
                accessUsage.TotalReceivedTraffic += usageParams.ReceivedTrafficByteCount;
                accessUsage.TotalSentTraffic += usageParams.SentTrafficByteCount;
                accessUsage.ModifiedTime = DateTime.Now;
                if (isConnecting) accessUsage.ConnectTime = DateTime.Now;
                vhContext.AccessUsages.Update(accessUsage);
            }

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

            // set expiration time on first use
            if (access.ExpirationTime == null && access.SentTrafficByteCount != 0 && access.ReceivedTrafficByteCount != 0 && accessToken.Lifetime != 0)
            {
                access.ExpirationTime = DateTime.Now.AddDays(accessToken.Lifetime);
                _logger.LogInformation($"Access has been activated! Expiration: {access.ExpirationTime}, ClientIdentity: {clientIdentity}");
            }

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
        [Route(nameof(GetAccess))]
        public async Task<Access> GetAccess(Guid serverId, AccessParams accessParams)
        {
            var clientIdentity = accessParams.ClientIdentity ?? throw new ArgumentException($"{nameof(AccessParams.ClientIdentity)} should not be null.", nameof(accessParams));
            _logger.LogInformation($"Getting Access. TokenId: {clientIdentity.TokenId}, ClientId: {clientIdentity.ClientId}");

            using VhContext vhContext = new();

            // Check AccountId
            // check accessToken, accessTokenGroup
            var query = from ATG in vhContext.AccessTokenGroups
                        join AT in vhContext.AccessTokens on ATG.AccessTokenGroupId equals AT.AccessTokenGroupId
                        join EP in vhContext.ServerEndPoints on ATG.AccessTokenGroupId equals EP.AccessTokenGroupId
                        where ATG.AccountId == AccountId && AT.AccessTokenId == clientIdentity.TokenId &&
                                (EP.PulicEndPoint == accessParams.RequestEndPoint.ToString() || EP.PrivateEndPoint == accessParams.RequestEndPoint.ToString())
                        select new { AT, EP.ServerId, EP.ServerEndPointId };
            var result = await query.SingleAsync();
            var accessToken = result.AT;

            // update client
            var client = await vhContext.Clients.FindAsync(clientIdentity.ClientId);
            if (client == null)
            {
                client = new Client
                {
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

            // update ServerEndPoint PublicIp
            if (result.ServerId != serverId)
            {
                vhContext.DebugMode = true; 
                var serverEndPoint = new ServerEndPoint() { ServerEndPointId = result.ServerEndPointId };
                vhContext.Entry(serverEndPoint).State = EntityState.Unchanged;
                vhContext.Entry(serverEndPoint).Property(x => x.ServerId).IsModified = true;
                await vhContext.SaveChangesAsync();



                //await vhContext.ServerEndPoints.SingleAsync(x => x.AccountId == AccountId && x.ServerEndPointId == result.ServerEndPointId);
                //serverEndPoint.ServerId = serverId;
                //vhContext.ServerEndPoints.Update(serverEndPoint);
                //vhContext.Entry(serverEndPoint).Property(x => x.CertificateRawData).IsModified = false;
            }

            // create return
            using var md5 = MD5.Create();
            var accessIdString = accessToken.IsPublic ? $"{accessToken.AccessTokenId},{clientIdentity.ClientId}" : accessToken.AccessTokenId.ToString();
            var accessId = Convert.ToBase64String(md5.ComputeHash(Encoding.UTF8.GetBytes(accessIdString)));
            var ret = await AddUsageInternal(vhContext, serverId, accessToken, new UsageParams { AccessId = accessId, ClientIdentity = accessParams.ClientIdentity }, true);

            await vhContext.SaveChangesAsync();
            return ret;
        }

        [HttpGet]
        [Route(nameof(GetSslCertificateData))]
        public async Task<byte[]> GetSslCertificateData(Guid serverId, string requestEndPoint)
        {
            using VhContext vhContext = new();
            var serverEndPoint = await vhContext.ServerEndPoints.SingleAsync(x => x.AccountId == AccountId && (x.PulicEndPoint == requestEndPoint || x.PrivateEndPoint == requestEndPoint));

            // update serverId associated with ServerEndPoint
            vhContext.DebugMode = true; //todo
            if (serverEndPoint.ServerId != serverId)
            {
                serverEndPoint.ServerId = serverId;
                vhContext.Entry(serverEndPoint).Property(x => x.CertificateRawData).IsModified = false;
                vhContext.ServerEndPoints.Update(serverEndPoint);
                await vhContext.SaveChangesAsync();
            }

            return serverEndPoint.CertificateRawData;
        }

        [HttpPost]
        [Route(nameof(SendServerStatus))]
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
                NatUdpCount= serverStatus.NatUdpCount,
                SessionCount = serverStatus.SessionCount,
                ThreadCount = serverStatus.ThreadCount,
            });
            await vhContext.SaveChangesAsync();
        }

        [HttpPost]
        [Route(nameof(Subscribe))]
        public async Task Subscribe(Guid serverId, ServerInfo serverInfo)
        {
            using VhContext vhContext = new();
            var server = await vhContext.Servers.FindAsync(serverId);
            var isNew = server == null;
            if (server == null)
            {
                server = new Models.Server() { 
                    AccountId = AccountId,
                    ServerId = serverId ,
                    CreatedTime = DateTime.Now
                };
            }
            else if (server.AccountId != AccountId)
                throw new AlreadyExistsException($"This serverId is used by another account! Change your server id. id: {serverId}");

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

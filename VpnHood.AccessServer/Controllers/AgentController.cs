using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Models;
using VpnHood.Common;
using VpnHood.Common.Messaging;
using VpnHood.Server;
using VpnHood.Server.Messaging;
using Access = VpnHood.AccessServer.Models.Access;
using AccessUsageEx = VpnHood.AccessServer.Models.AccessUsageEx;

namespace VpnHood.AccessServer.Controllers
{
    [ApiController]
    [Route("/api/agent")]
    [Authorize(AuthenticationSchemes = "Robot")]
    public class AgentController : ControllerBase
    {
        protected readonly ILogger<AgentController> Logger;
        public AgentController(ILogger<AgentController> logger)
        {
            Logger = logger;
        }

        private async Task<Models.Server> GetServer(VhContext vhContext, bool includeAccessPoints = false)
        {
            // find serverId from identity claims
            var subject = User.Claims.FirstOrDefault(claim => claim.Type == ClaimTypes.NameIdentifier)?.Value ?? throw new UnauthorizedAccessException();
            if (!Guid.TryParse(subject, out var serverId))
                throw new UnauthorizedAccessException();

            // find authorizationCode from identity claims
            var authorizationCodeStr = User.Claims.FirstOrDefault(claim => claim.Type == "authorization_code")?.Value;
            if (!Guid.TryParse(authorizationCodeStr, out var authorizationCode))
                throw new UnauthorizedAccessException();

            var query = (IQueryable<Models.Server>)vhContext.Servers;
            if (includeAccessPoints)
                query = query.Include(x => x.AccessPoints);

            var server = await query.SingleAsync(x =>
                x.ServerId == serverId &&
                x.AuthorizationCode == authorizationCode);
            return server;
        }

        private static bool ValidateRequest(SessionRequest sessionRequest, byte[] tokenSecret)
        {
            var encryptClientId = Util.EncryptClientId(sessionRequest.ClientInfo.ClientId, tokenSecret);
            return encryptClientId.SequenceEqual(sessionRequest.EncryptedClientId);
        }

        private static SessionResponseEx BuildSessionResponse(VhContext vhContext, Session session,
            AccessToken accessToken, Access access, AccessUsageEx? accessUsage)
        {
            // create common accessUsage
            var accessUsage2 = new AccessUsage
            {
                MaxClientCount = accessToken.MaxDevice,
                MaxTraffic = accessToken.MaxTraffic,
                ExpirationTime = access.EndTime,
                SentTraffic = accessUsage?.CycleSentTraffic ?? 0,
                ReceivedTraffic = accessUsage?.CycleReceivedTraffic ?? 0,
                ActiveClientCount = 0
            };

            // validate session status
            if (session.ErrorCode == SessionErrorCode.Ok)
            {
                // check token expiration
                if (accessUsage2.ExpirationTime != null && accessUsage2.ExpirationTime < DateTime.UtcNow)
                    return new SessionResponseEx(SessionErrorCode.AccessExpired)
                    { AccessUsage = accessUsage2, ErrorMessage = "Access Expired!" };

                // check traffic
                if (accessUsage2.MaxTraffic != 0 &&
                    accessUsage2.SentTraffic + accessUsage2.ReceivedTraffic > accessUsage2.MaxTraffic)
                    return new SessionResponseEx(SessionErrorCode.AccessTrafficOverflow)
                    { AccessUsage = accessUsage2, ErrorMessage = "All traffic quota has been consumed!" };

                var otherSessions = vhContext.Sessions
                    .Where(x => x.EndTime == null && x.AccessId == session.AccessId)
                    .OrderBy(x => x.CreatedTime).ToArray();

                // suppressedTo yourself
                var selfSessions = otherSessions.Where(x =>
                    x.DeviceId == session.DeviceId && x.SessionId != session.SessionId).ToArray();
                if (selfSessions.Any())
                {
                    session.SuppressedTo = SessionSuppressType.YourSelf;
                    foreach (var selfSession in selfSessions)
                    {
                        selfSession.SuppressedBy = SessionSuppressType.YourSelf;
                        selfSession.ErrorCode = SessionErrorCode.SessionSuppressedBy;
                        selfSession.EndTime = DateTime.UtcNow;
                        vhContext.Sessions.Update(selfSession);
                    }
                }

                // suppressedTo others by MaxClientCount
                if (accessUsage2.MaxClientCount != 0)
                {
                    var otherSessions2 = otherSessions
                        .Where(x => x.DeviceId != session.DeviceId && x.SessionId != session.SessionId)
                        .OrderBy(x => x.CreatedTime).ToArray();
                    for (var i = 0; i <= otherSessions2.Length - accessUsage2.MaxClientCount; i++)
                    {
                        var otherSession = otherSessions2[i];
                        otherSession.SuppressedBy = SessionSuppressType.Other;
                        otherSession.ErrorCode = SessionErrorCode.SessionSuppressedBy;
                        otherSession.EndTime = DateTime.UtcNow;
                        session.SuppressedTo = SessionSuppressType.Other;
                        vhContext.Sessions.Update(otherSession);
                    }
                }

                accessUsage2.ActiveClientCount = accessToken.IsPublic ? 0 : otherSessions.Count(x => x.EndTime == null);
            }

            // build result
            return new SessionResponseEx(SessionErrorCode.Ok)
            {
                SessionId = (uint)session.SessionId,
                CreatedTime = session.CreatedTime,
                SessionKey = session.SessionKey,
                SuppressedTo = session.SuppressedTo,
                SuppressedBy = session.SuppressedBy,
                ErrorCode = session.ErrorCode,
                ErrorMessage = session.ErrorMessage,
                AccessUsage = accessUsage2,
                RedirectHostEndPoint = null
            };
        }

        [HttpPost("sessions")]
        public async Task<SessionResponseEx> Session_Create(SessionRequestEx sessionRequestEx)
        {
            var clientIp = sessionRequestEx.ClientIp?.ToString();
            var clientInfo = sessionRequestEx.ClientInfo;
            var requestEndPoint = sessionRequestEx.HostEndPoint;
            var anyIp = requestEndPoint.AddressFamily == AddressFamily.InterNetworkV6
                ? IPAddress.IPv6Any
                : IPAddress.Any;

            await using var vhContext = new VhContext();
            var server = await GetServer(vhContext);

            // Get accessToken and check projectId, accessToken
            var query = from accessPointGroup in vhContext.AccessPointGroups
                        join at in vhContext.AccessTokens on accessPointGroup.AccessPointGroupId equals at.AccessPointGroupId
                        join accessPoint in vhContext.AccessPoints on accessPointGroup.AccessPointGroupId equals accessPoint.AccessPointGroupId
                        where accessPointGroup.ProjectId == server.ProjectId &&
                              accessPoint.ServerId == server.ServerId &&
                              at.AccessTokenId == sessionRequestEx.TokenId &&
                              accessPoint.IsListen &&
                              accessPoint.TcpPort == requestEndPoint.Port &&
                              (accessPoint.IpAddress == anyIp.ToString() || accessPoint.IpAddress == requestEndPoint.Address.ToString())
                        select new { acToken = at, accessPoint.ServerId };
            var result = await query.SingleAsync();
            var accessToken = result.acToken;

            // validate the request
            if (!ValidateRequest(sessionRequestEx, accessToken.Secret))
                return new SessionResponseEx(SessionErrorCode.GeneralError)
                { ErrorMessage = "Could not validate the request!" };

            // create client or update if changed
            var device = await vhContext.Devices.SingleOrDefaultAsync(x => x.ProjectId == server.ProjectId && x.ClientId == clientInfo.ClientId);
            if (device == null)
            {
                device = new Device
                {
                    DeviceId = Guid.NewGuid(),
                    ProjectId = server.ProjectId,
                    ClientId = clientInfo.ClientId,
                    DeviceIp = clientIp,
                    ClientVersion = clientInfo.ClientVersion,
                    UserAgent = clientInfo.UserAgent,
                    CreatedTime = DateTime.UtcNow
                };
                await vhContext.Devices.AddAsync(device);
            }
            else if (device.UserAgent != clientInfo.UserAgent || device.ClientVersion != clientInfo.ClientVersion ||
                     device.DeviceIp != clientIp)
            {
                device.UserAgent = clientInfo.UserAgent;
                device.ClientVersion = clientInfo.ClientVersion;
                device.DeviceIp = clientIp;
                vhContext.Devices.Update(device);
            }

            // get or create access
            Guid? deviceId = accessToken.IsPublic ? device.DeviceId : null;
            var res = await (
                from a in vhContext.Accesses
                join au in vhContext.AccessUsages on new { key1 = a.AccessId, key2 = true } equals new { key1 = au.AccessId, key2 = au.IsLast } into grouping
                from au in grouping.DefaultIfEmpty()
                where a.AccessTokenId == accessToken.AccessTokenId && a.DeviceId == deviceId
                select new { a, au }
            ).SingleOrDefaultAsync();

            var access = res?.a;
            var accessUsage = res?.au;

            if (access == null)
            {
                access = new Access
                {
                    AccessId = Guid.NewGuid(),
                    AccessTokenId = sessionRequestEx.TokenId,
                    DeviceId = accessToken.IsPublic ? device.DeviceId : null,
                    CreatedTime = DateTime.UtcNow,
                    EndTime = accessToken.EndTime
                };

                // set accessToken expiration time on first use
                if (accessToken.EndTime == null && accessToken.Lifetime != 0)
                    access.EndTime = DateTime.UtcNow.AddDays(accessToken.Lifetime);

                Logger.LogInformation($"Access has been activated! AccessId: {access.AccessId}");
                await vhContext.Accesses.AddAsync(access);
            }
            else
            {
                if (accessUsage != null)
                {
                    accessUsage.IsLast = false;
                    vhContext.AccessUsages.Update(accessUsage);
                }
            }

            // create session
            var session = new Session
            {
                SessionKey = Util.GenerateSessionKey(),
                CreatedTime = DateTime.UtcNow,
                AccessedTime = DateTime.UtcNow,
                AccessTokenId = accessToken.AccessTokenId,
                AccessId = access.AccessId,
                DeviceIp = clientIp,
                DeviceId = device.DeviceId,
                ClientVersion = device.ClientVersion,
                EndTime = null,
                ServerId = server.ServerId,
                SuppressedBy = SessionSuppressType.None,
                SuppressedTo = SessionSuppressType.None,
                ErrorCode = SessionErrorCode.Ok,
                ErrorMessage = null
            };

            var ret = BuildSessionResponse(vhContext, session, accessToken, access, accessUsage);
            if (ret.ErrorCode != SessionErrorCode.Ok)
                return ret;

            session = (await vhContext.Sessions.AddAsync(session)).Entity;

            await vhContext.Database.BeginTransactionAsync();
            await vhContext.SaveChangesAsync();

            // insert AccessUsageLog
            await vhContext.AccessUsages.AddAsync(new AccessUsageEx
            {
                AccessId = session.AccessId,
                SessionId = (uint)session.SessionId,
                ReceivedTraffic = 0,
                SentTraffic = 0,
                CycleReceivedTraffic = accessUsage?.CycleReceivedTraffic ?? 0,
                CycleSentTraffic = accessUsage?.CycleSentTraffic ?? 0,
                TotalReceivedTraffic = accessUsage?.TotalReceivedTraffic ?? 0,
                TotalSentTraffic = accessUsage?.TotalSentTraffic ?? 0,
                ServerId = server.ServerId,
                CreatedTime = DateTime.UtcNow,
                IsLast = true
            });
            await vhContext.SaveChangesAsync();
            await vhContext.Database.CommitTransactionAsync();

            ret.SessionId = (uint)session.SessionId;
            return ret;
        }

        [HttpGet("sessions/{sessionId}")]
        public async Task<SessionResponseEx> Session_Get(uint sessionId, string hostEndPoint, string? clientIp)
        {
            _ = clientIp;
            var requestEndPoint = IPEndPoint.Parse(hostEndPoint);
            var anyIp = requestEndPoint.AddressFamily == AddressFamily.InterNetworkV6
                ? IPAddress.IPv6Any
                : IPAddress.Any;

            await using var vhContext = new VhContext();
            var server = await GetServer(vhContext);

            // make sure hostEndPoint is accessible by this session
            var query = from atg in vhContext.AccessPointGroups
                        join at in vhContext.AccessTokens on atg.AccessPointGroupId equals at.AccessPointGroupId
                        join a in vhContext.Accesses on at.AccessTokenId equals a.AccessTokenId
                        join s in vhContext.Sessions on a.AccessId equals s.AccessId
                        join accessPoint in vhContext.AccessPoints on atg.AccessPointGroupId equals accessPoint.AccessPointGroupId
                        join au in vhContext.AccessUsages on new { key1 = a.AccessId, key2 = true } equals new { key1 = au.AccessId, key2 = au.IsLast } into grouping
                        from au in grouping.DefaultIfEmpty()
                        where at.ProjectId == server.ProjectId &&
                              accessPoint.ServerId == server.ServerId &&
                              s.SessionId == sessionId &&
                              a.AccessId == s.AccessId &&
                              accessPoint.IsListen &&
                              accessPoint.TcpPort == requestEndPoint.Port &&
                              (accessPoint.IpAddress == anyIp.ToString() || accessPoint.IpAddress == requestEndPoint.Address.ToString())
                        select new { at, a, s, au };
            var result = await query.SingleAsync();

            var accessToken = result.at;
            var access = result.a;
            var accessUsage = result.au;
            var session = result.s;

            // build response
            var ret = BuildSessionResponse(vhContext, session, accessToken, access, accessUsage);

            // update session AccessedTime
            result.s.AccessedTime = DateTime.UtcNow;
            vhContext.Sessions.Update(session);
            await vhContext.SaveChangesAsync();

            return ret;
        }

        [HttpPost("sessions/{sessionId}/usage")]
        public async Task<ResponseBase> Session_AddUsage(uint sessionId, UsageInfo usageInfo,
            bool closeSession = false)
        {
            await using var vhContext = new VhContext();
            var server = await GetServer(vhContext);

            // make sure hostEndPoint is accessible by this session
            var query = from at in vhContext.AccessTokens
                        join a in vhContext.Accesses on at.AccessTokenId equals a.AccessTokenId
                        join s in vhContext.Sessions on a.AccessId equals s.AccessId
                        join au in vhContext.AccessUsages on new { key1 = a.AccessId, key2 = true } equals new { key1 = au.AccessId, key2 = au.IsLast } into grouping
                        from au in grouping.DefaultIfEmpty()
                        where at.ProjectId == server.ProjectId && s.SessionId == sessionId && a.AccessId == s.AccessId
                        select new { at, a, s, au };
            var result = await query.SingleAsync();

            var accessToken = result.at;
            var access = result.a;
            var accessUsage = result.au;
            var session = result.s;

            // add usage 
            await vhContext.Database.BeginTransactionAsync();
            Logger.LogInformation($"AddUsage to {access.AccessId}, SentTraffic: {usageInfo.SentTraffic / 1000000} MB, ReceivedTraffic: {usageInfo.ReceivedTraffic / 1000000} MB");
            if (accessUsage != null)
            {
                accessUsage.IsLast = false;
                vhContext.AccessUsages.Update(accessUsage);
                await vhContext.SaveChangesAsync();
            }

            // insert AccessUsageLog
            var addRes = await vhContext.AccessUsages.AddAsync(new AccessUsageEx
            {
                AccessId = session.AccessId,
                SessionId = (uint)session.SessionId,
                ReceivedTraffic = usageInfo.ReceivedTraffic,
                SentTraffic = usageInfo.SentTraffic,
                CycleReceivedTraffic = (accessUsage?.CycleReceivedTraffic ?? 0) + usageInfo.ReceivedTraffic,
                CycleSentTraffic = (accessUsage?.CycleSentTraffic ?? 0) + usageInfo.SentTraffic,
                TotalReceivedTraffic = (accessUsage?.TotalReceivedTraffic ?? 0) + usageInfo.ReceivedTraffic,
                TotalSentTraffic = (accessUsage?.TotalSentTraffic ?? 0) + usageInfo.SentTraffic,
                ServerId = server.ServerId,
                CreatedTime = DateTime.UtcNow,
                IsLast = true
            });
            accessUsage = addRes.Entity;

            // build response
            var ret = BuildSessionResponse(vhContext, session, accessToken, access, accessUsage);

            // close session
            if (closeSession && ret.ErrorCode == SessionErrorCode.Ok)
            {
                session.ErrorCode = SessionErrorCode.SessionClosed;
                session.EndTime = DateTime.UtcNow;
            }

            // update session
            session.AccessedTime = DateTime.UtcNow;
            vhContext.Sessions.Update(session);

            await vhContext.SaveChangesAsync();
            await vhContext.Database.CommitTransactionAsync();
            return new ResponseBase(ret);
        }

        [HttpGet("certificates/{hostEndPoint}")]
        public async Task<byte[]> GetSslCertificateData(string hostEndPoint)
        {
            await using var vhContext = new VhContext();
            var server = await GetServer(vhContext);

            var requestEndPoint = IPEndPoint.Parse(hostEndPoint);
            var anyIp = requestEndPoint.AddressFamily == AddressFamily.InterNetworkV6
                ? IPAddress.IPv6Any
                : IPAddress.Any;

            var accessPoint = await
                vhContext.AccessPoints
                .Include(x => x.AccessPointGroup)
                .Include(x => x.AccessPointGroup!.Certificate)
                .SingleAsync(x => x.ServerId == server.ServerId &&
                                  x.IsListen &&
                                  x.TcpPort == requestEndPoint.Port &&
                                  (x.IpAddress == anyIp.ToString() || x.IpAddress == requestEndPoint.Address.ToString()));


            return accessPoint.AccessPointGroup!.Certificate!.RawData;
        }

        [HttpPost("status")]
        public async Task<ServerCommand> UpdateServerStatus(ServerStatus serverStatus)
        {
            // get current accessToken
            await PublicCycleHelper.UpdateCycle(); //todo: move to a job

            await using var vhContext = new VhContext();
            var server = await GetServer(vhContext);
            await InsertServerStatus(vhContext, server, serverStatus, false);
            await vhContext.SaveChangesAsync();

            var ret = new ServerCommand()
            {
                ConfigCode = server.ConfigCode
            };

            return ret;
        }

        private static async Task InsertServerStatus(VhContext vhContext, Models.Server server,
            ServerStatus serverStatus, bool isConfigure)
        {
            var serverStatusLog = await vhContext.ServerStatus.SingleOrDefaultAsync(x => x.ServerId == server.ServerId && x.IsLast);

            // remove IsLast
            if (serverStatusLog != null)
            {
                serverStatusLog.IsLast = false;
                vhContext.Update(serverStatusLog);
            }

            await vhContext.ServerStatus.AddAsync(new ServerStatusEx
            {
                ServerId = server.ServerId,
                IsConfigure = isConfigure,
                IsLast = true,
                CreatedTime = DateTime.UtcNow,
                FreeMemory = serverStatus.FreeMemory,
                TcpConnectionCount = serverStatus.TcpConnectionCount,
                UdpConnectionCount = serverStatus.UdpConnectionCount,
                SessionCount = serverStatus.SessionCount,
                ThreadCount = serverStatus.ThreadCount,
                ReceivingBandwith = serverStatus.ReceivingBandwith,
                SendingBandwith = serverStatus.SendingBandwith
            });
        }

        [HttpPost("configure")]
        public async Task<ServerConfig> ConfigureServer(ServerInfo serverInfo)
        {
            await using var vhContext = new VhContext();
            var server = await GetServer(vhContext, true);

            // update server
            server.EnvironmentVersion = serverInfo.EnvironmentVersion.ToString();
            server.OsInfo = serverInfo.OsInfo;
            server.MachineName = serverInfo.MachineName;
            server.ConfigureTime = DateTime.UtcNow;
            server.TotalMemory = serverInfo.TotalMemory;
            server.Version = serverInfo.Version.ToString();
            if (server.ConfigCode == serverInfo.ConfigCode) server.ConfigCode = null;
            vhContext.Update(server);
            await InsertServerStatus(vhContext, server, serverInfo.Status, true);

            // check is Access
            if (server.AccessPointGroupId != null)
                await UpdateServerAccessPoints(vhContext, server, serverInfo);

            await vhContext.SaveChangesAsync();

            // read server accessPoints
            var accessPoints = await vhContext.AccessPoints
                .Where(x => x.ServerId == server.ServerId && x.IsListen)
                .ToArrayAsync();

            var ipEndPoints = accessPoints.Select(x => new IPEndPoint(IPAddress.Parse(x.IpAddress), x.TcpPort)).ToArray();
            var ret = new ServerConfig(ipEndPoints)
            {
                UpdateStatusInterval = AccessServerApp.Instance.ServerUpdateStatusInverval
            };

            return ret;
        }

        private static bool AccessPointEquals(AccessPoint value1, AccessPoint value2)
        {
            return
                value1.ServerId.Equals(value2.ServerId) &&
                value1.IpAddress.Equals(value2.IpAddress) &&
                value1.IsListen.Equals(value2.IsListen) &&
                value1.AccessPointGroupId.Equals(value2.AccessPointGroupId) &&
                value1.AccessPointMode.Equals(value2.AccessPointMode) &&
                value1.TcpPort.Equals(value2.TcpPort) &&
                value1.UdpPort.Equals(value2.UdpPort);
        }

        private static async Task UpdateServerAccessPoints(VhContext vhContext, Models.Server server, ServerInfo serverInfo)
        {
            if (server.AccessPointGroupId == null) throw new InvalidOperationException($"{nameof(server.AccessPointGroupId)} is not set!");

            // find current tokenAccessPoints in AccessPointGroup
            var tokenAccessPoints = await vhContext.AccessPoints.Where(x =>
                x.AccessPointGroupId == server.AccessPointGroupId &&
                x.AccessPointMode == AccessPointMode.PublicInToken)
                .ToArrayAsync();

            var accessPoints = new List<AccessPoint>();

            // create private addresses
            foreach (var ipAddress in serverInfo.PrivateIpAddresses)
            {
                if (serverInfo.PublicIpAddresses.Any(x => x.Equals(ipAddress)))
                    continue; // will added by public address as listener

                var accessPoint = new AccessPoint
                {
                    AccessPointId = Guid.NewGuid(),
                    ServerId = server.ServerId,
                    AccessPointGroupId = server.AccessPointGroupId.Value,
                    AccessPointMode = AccessPointMode.Private,
                    IsListen = true,
                    IpAddress = ipAddress.ToString(),
                    TcpPort = 443,
                    UdpPort = 0
                };
                accessPoints.Add(accessPoint);
            }

            // create public addresses
            accessPoints.AddRange(serverInfo.PublicIpAddresses
                    .Distinct()
                    .Select(ipAddress => new AccessPoint
                    {
                        AccessPointId = Guid.NewGuid(),
                        ServerId = server.ServerId,
                        AccessPointGroupId = server.AccessPointGroupId.Value,
                        AccessPointMode = tokenAccessPoints.Any(x => IPAddress.Parse(x.IpAddress).Equals(ipAddress))
                            ? AccessPointMode.PublicInToken : AccessPointMode.Public, // prefer last value
                        IsListen = serverInfo.PrivateIpAddresses.Any(x => x.Equals(ipAddress)),
                        IpAddress = ipAddress.ToString(),
                        TcpPort = 443,
                        UdpPort = 0
                    }));

            // Select first publicIp as a tokenAccessPoint if there is no tokenAccessPoint in other server of same group
            var firstPublicAccessPoint = accessPoints.FirstOrDefault(x => x.AccessPointMode == AccessPointMode.Public);
            if (tokenAccessPoints.All(x => x.ServerId == server.ServerId) &&
                accessPoints.All(x => x.AccessPointMode != AccessPointMode.PublicInToken) &&
                firstPublicAccessPoint != null)
                firstPublicAccessPoint.AccessPointMode = AccessPointMode.PublicInToken;

            // start syncing
            var curAccessPoints = server.AccessPoints?.ToArray() ?? Array.Empty<AccessPoint>();
            vhContext.AccessPoints.RemoveRange(curAccessPoints.Where(x => !accessPoints.Any(y => AccessPointEquals(x, y))));
            await vhContext.AccessPoints.AddRangeAsync(accessPoints.Where(x => !curAccessPoints.Any(y => AccessPointEquals(x, y))));
        }
    }
}
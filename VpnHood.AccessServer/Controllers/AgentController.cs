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

            var server = await query.SingleOrDefaultAsync(x =>
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
            AccessToken accessToken, Access access)
        {
            // create common accessUsage
            var accessUsage = new AccessUsage
            {
                MaxClientCount = accessToken.MaxClient,
                MaxTraffic = accessToken.MaxTraffic,
                ExpirationTime = access.EndTime,
                SentTraffic = access.CycleSentTraffic,
                ReceivedTraffic = access.CycleReceivedTraffic,
                ActiveClientCount = 0
            };

            // validate session status
            if (session.ErrorCode == SessionErrorCode.Ok)
            {
                // check token expiration
                if (accessUsage.ExpirationTime != null && accessUsage.ExpirationTime < DateTime.UtcNow)
                    return new SessionResponseEx(SessionErrorCode.AccessExpired)
                    { AccessUsage = accessUsage, ErrorMessage = "Access Expired!" };

                // check traffic
                if (accessUsage.MaxTraffic != 0 &&
                    accessUsage.SentTraffic + accessUsage.ReceivedTraffic > accessUsage.MaxTraffic)
                    return new SessionResponseEx(SessionErrorCode.AccessTrafficOverflow)
                    { AccessUsage = accessUsage, ErrorMessage = "All traffic quota has been consumed!" };

                var otherSessions = vhContext.Sessions
                    .Where(x => x.EndTime == null && x.AccessId == session.AccessId)
                    .OrderBy(x => x.CreatedTime).ToArray();

                // suppressedTo yourself
                var selfSessions = otherSessions.Where(x =>
                    x.ProjectClientId == session.ProjectClientId && x.SessionId != session.SessionId).ToArray();
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
                if (accessUsage.MaxClientCount != 0)
                {
                    var otherSessions2 = otherSessions
                        .Where(x => x.ProjectClientId != session.ProjectClientId && x.SessionId != session.SessionId)
                        .OrderBy(x => x.CreatedTime).ToArray();
                    for (var i = 0; i <= otherSessions2.Length - accessUsage.MaxClientCount; i++)
                    {
                        var otherSession = otherSessions2[i];
                        otherSession.SuppressedBy = SessionSuppressType.Other;
                        otherSession.ErrorCode = SessionErrorCode.SessionSuppressedBy;
                        otherSession.EndTime = DateTime.UtcNow;
                        session.SuppressedTo = SessionSuppressType.Other;
                        vhContext.Sessions.Update(otherSession);
                    }
                }

                accessUsage.ActiveClientCount = accessToken.IsPublic ? 0 : otherSessions.Count(x => x.EndTime == null);
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
                AccessUsage = accessUsage,
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
            var projectClient =
                await vhContext.ProjectClients.SingleOrDefaultAsync(x =>
                    x.ProjectId == server.ProjectId && x.ClientId == clientInfo.ClientId);
            if (projectClient == null)
            {
                projectClient = new ProjectClient
                {
                    ProjectClientId = Guid.NewGuid(),
                    ProjectId = server.ProjectId,
                    ClientId = clientInfo.ClientId,
                    ClientIp = clientIp,
                    ClientVersion = clientInfo.ClientVersion,
                    UserAgent = clientInfo.UserAgent,
                    CreatedTime = DateTime.UtcNow
                };
                await vhContext.ProjectClients.AddAsync(projectClient);
            }
            else if (projectClient.UserAgent != clientInfo.UserAgent || projectClient.ClientVersion != clientInfo.ClientVersion ||
                     projectClient.ClientIp != clientIp)
            {
                projectClient.UserAgent = clientInfo.UserAgent;
                projectClient.ClientVersion = clientInfo.ClientVersion;
                projectClient.ClientIp = clientIp;
                vhContext.ProjectClients.Update(projectClient);
            }

            // get or create accessUsage
            Guid? projectClientId = accessToken.IsPublic ? projectClient.ProjectClientId : null;
            var access = await vhContext.Accesses.SingleOrDefaultAsync(x =>
                x.AccessTokenId == accessToken.AccessTokenId && x.ProjectClientId == projectClientId);
            if (access == null)
            {
                access = new Access
                {
                    AccessId = Guid.NewGuid(),
                    AccessTokenId = sessionRequestEx.TokenId,
                    ProjectClientId = accessToken.IsPublic ? projectClient.ProjectClientId : null,
                    CreatedTime = DateTime.UtcNow,
                    ModifiedTime = DateTime.UtcNow,
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
                access.ModifiedTime = DateTime.UtcNow;
                vhContext.Accesses.Update(access);
            }

            // create session
            Session session = new()
            {
                SessionKey = Util.GenerateSessionKey(),
                CreatedTime = DateTime.UtcNow,
                AccessedTime = DateTime.UtcNow,
                AccessId = access.AccessId,
                ClientIp = clientIp,
                ProjectClientId = projectClient.ProjectClientId,
                ClientVersion = projectClient.ClientVersion,
                EndTime = null,
                ServerId = server.ServerId,
                SuppressedBy = SessionSuppressType.None,
                SuppressedTo = SessionSuppressType.None,
                ErrorCode = SessionErrorCode.Ok,
                ErrorMessage = null
            };

            var ret = BuildSessionResponse(vhContext, session, accessToken, access);
            if (ret.ErrorCode != SessionErrorCode.Ok)
                return ret;

            vhContext.Sessions.Add(session);
            await vhContext.SaveChangesAsync();
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
                        join au in vhContext.Accesses on at.AccessTokenId equals au.AccessTokenId
                        join s in vhContext.Sessions on au.AccessId equals s.AccessId
                        join accessPoint in vhContext.AccessPoints on atg.AccessPointGroupId equals accessPoint.AccessPointGroupId
                        where at.ProjectId == server.ProjectId &&
                              accessPoint.ServerId == server.ServerId &&
                              s.SessionId == sessionId &&
                              au.AccessId == s.AccessId &&
                              accessPoint.IsListen &&
                              accessPoint.TcpPort == requestEndPoint.Port &&
                              (accessPoint.IpAddress == anyIp.ToString() || accessPoint.IpAddress == requestEndPoint.Address.ToString())
                        select new { at, au, s };
            var result = await query.SingleAsync();

            var accessToken = result.at;
            var accessUsage = result.au;
            var session = result.s;

            // build response
            var ret = BuildSessionResponse(vhContext, session, accessToken, accessUsage);

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
                        join au in vhContext.Accesses on at.AccessTokenId equals au.AccessTokenId
                        join s in vhContext.Sessions on au.AccessId equals s.AccessId
                        where at.ProjectId == server.ProjectId && s.SessionId == sessionId && au.AccessId == s.AccessId
                        select new { at, au, s };
            var result = await query.SingleAsync();

            var accessToken = result.at;
            var accessUsage = result.au;
            var session = result.s;

            // add usage 
            Logger.LogInformation(
                $"AddUsage to {accessUsage.AccessId}, SentTraffic: {usageInfo.SentTraffic / 1000000} MB, ReceivedTraffic: {usageInfo.ReceivedTraffic / 1000000} MB");
            accessUsage.CycleSentTraffic += usageInfo.SentTraffic;
            accessUsage.CycleReceivedTraffic += usageInfo.ReceivedTraffic;
            accessUsage.TotalSentTraffic += usageInfo.SentTraffic;
            accessUsage.TotalReceivedTraffic += usageInfo.ReceivedTraffic;
            accessUsage.ModifiedTime = DateTime.UtcNow;
            vhContext.Accesses.Update(accessUsage);

            // insert AccessUsageLog
            await vhContext.AccessLogs.AddAsync(new AccessLog
            {
                SessionId = (uint)session.SessionId,
                ReceivedTraffic = usageInfo.ReceivedTraffic,
                SentTraffic = usageInfo.SentTraffic,
                CycleReceivedTraffic = accessUsage.CycleReceivedTraffic,
                CycleSentTraffic = accessUsage.CycleSentTraffic,
                TotalReceivedTraffic = accessUsage.TotalReceivedTraffic,
                TotalSentTraffic = accessUsage.TotalSentTraffic,
                ServerId = server.ServerId,
                CreatedTime = DateTime.UtcNow
            });

            // update accessedTime

            // build response
            var ret = BuildSessionResponse(vhContext, session, accessToken, accessUsage);

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
        public async Task UpdateServerStatus(ServerStatus serverStatus)
        {
            // get current accessToken
            await PublicCycleHelper.UpdateCycle(); //todo: move to a job

            await using var vhContext = new VhContext();
            var server = await GetServer(vhContext);
            await InsertServerStatus(vhContext, server, serverStatus, false);
            await vhContext.SaveChangesAsync();
        }

        private static async Task InsertServerStatus(VhContext vhContext, Models.Server server,
            ServerStatus serverStatus, bool isConfigure)
        {
            var serverStatusLog = await vhContext.ServerStatusLogs.SingleOrDefaultAsync(x => x.ServerId == server.ServerId && x.IsLast);

            // remove IsLast
            if (serverStatusLog != null)
            {
                serverStatusLog.IsLast = false;
                vhContext.Update(serverStatusLog);
            }

            await vhContext.ServerStatusLogs.AddAsync(new ServerStatusLog
            {
                ServerId = server.ServerId,
                IsConfigure = isConfigure,
                IsLast = true,
                CreatedTime = DateTime.UtcNow,
                FreeMemory = serverStatus.FreeMemory,
                TcpConnectionCount = serverStatus.TcpConnectionCount,
                UdpConnectionCount = serverStatus.UdpConnectionCount,
                SessionCount = serverStatus.SessionCount,
                ThreadCount = serverStatus.ThreadCount
            });
        }

        [HttpPost("configure")]
        public async Task ServerConfigure(ServerInfo serverInfo)
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
            vhContext.Update(server);
            await InsertServerStatus(vhContext, server, serverInfo.Status, true);

            // check is Access
            if (server.AccessPointGroupId != null)
                await UpdateServerAccessPoints(vhContext, server, serverInfo);

            await vhContext.SaveChangesAsync();
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
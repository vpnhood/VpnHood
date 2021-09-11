using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Exceptions;
using VpnHood.AccessServer.Models;
using VpnHood.Common;
using VpnHood.Common.Messaging;
using VpnHood.Server;
using VpnHood.Server.Messaging;
using Access = VpnHood.AccessServer.Models.Access;

namespace VpnHood.AccessServer.Controllers
{
    [Route("/api/access")]
    public class AccessController : SuperController<AccessController>
    {
        public AccessController(ILogger<AccessController> logger) : base(logger)
        {
        }

        private Guid ProjectId
        {
            get
            {
                var res = User.Claims.FirstOrDefault(claim => claim.Type == "project_id")?.Value ??
                          throw new UnauthorizedAccessException();
                return Guid.Parse(res);
            }
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
        public async Task<SessionResponseEx> Session_Create(Guid serverId, SessionRequestEx sessionRequestEx)
        {
            var hostEndPoint = sessionRequestEx.HostEndPoint.ToString();
            var clientIp = sessionRequestEx.ClientIp?.ToString();
            var clientInfo = sessionRequestEx.ClientInfo;

            await using VhContext vhContext = new();

            // Get accessToken and check projectId, accessToken, accessPointGroup
            var query = from atg in vhContext.AccessPointGroups
                        join at in vhContext.AccessTokens on atg.AccessPointGroupId equals at.AccessPointGroupId
                        join ep in vhContext.AccessPoints on atg.AccessPointGroupId equals ep.AccessPointGroupId
                        where atg.ProjectId == ProjectId && at.AccessTokenId == sessionRequestEx.TokenId &&
                              (ep.PublicEndPoint == hostEndPoint || ep.PrivateEndPoint == hostEndPoint)
                        select new { at, ep.ServerId, ep.AccessPointId };
            var result = await query.SingleAsync();
            var accessToken = result.at;

            // validate the request
            if (!ValidateRequest(sessionRequestEx, accessToken.Secret))
                return new SessionResponseEx(SessionErrorCode.GeneralError)
                { ErrorMessage = "Could not validate the request!" };

            // create client or update if changed
            var projectClient =
                await vhContext.ProjectClients.SingleOrDefaultAsync(x =>
                    x.ProjectId == ProjectId && x.ClientId == clientInfo.ClientId);
            if (projectClient == null)
            {
                projectClient = new ProjectClient
                {
                    ProjectClientId = Guid.NewGuid(),
                    ProjectId = ProjectId,
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

            // update AccessPoint.ServerId if changed
            if (result.ServerId != serverId)
            {
                var accessPoint = await vhContext.AccessPoints.SingleAsync(x =>
                    x.ProjectId == ProjectId && x.AccessPointId == result.AccessPointId);
                accessPoint.ServerId = serverId;
                vhContext.AccessPoints.Update(accessPoint);
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
                ServerId = serverId,
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
        public async Task<SessionResponseEx> Session_Get(Guid serverId, uint sessionId, string hostEndPoint, string? clientIp)
        {
            _ = clientIp;
            _ = serverId;
            hostEndPoint = AccessUtil.ValidateIpEndPoint(hostEndPoint);
            await using VhContext vhContext = new();

            // make sure hostEndPoint is accessible by this session
            var query = from atg in vhContext.AccessPointGroups
                        join at in vhContext.AccessTokens on atg.AccessPointGroupId equals at.AccessPointGroupId
                        join au in vhContext.Accesses on at.AccessTokenId equals au.AccessTokenId
                        join s in vhContext.Sessions on au.AccessId equals s.AccessId
                        join ep in vhContext.AccessPoints on atg.AccessPointGroupId equals ep.AccessPointGroupId
                        where at.ProjectId == ProjectId && s.SessionId == sessionId && au.AccessId == s.AccessId &&
                              (ep.PublicEndPoint == hostEndPoint || ep.PrivateEndPoint == hostEndPoint)
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
        public async Task<ResponseBase> Session_AddUsage(Guid serverId, uint sessionId, UsageInfo usageInfo,
            bool closeSession = false)
        {
            await using VhContext vhContext = new();

            // make sure hostEndPoint is accessible by this session
            var query = from at in vhContext.AccessTokens
                        join au in vhContext.Accesses on at.AccessTokenId equals au.AccessTokenId
                        join s in vhContext.Sessions on au.AccessId equals s.AccessId
                        where at.ProjectId == ProjectId && s.SessionId == sessionId && au.AccessId == s.AccessId
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
                ServerId = serverId,
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

        [HttpGet("ssl-certificates/{hostEndPoint}")]
        public async Task<byte[]> GetSslCertificateData(Guid serverId, string hostEndPoint)
        {
            await using VhContext vhContext = new();

            var accessPoint = await vhContext.AccessPoints
                .Include(x => x.AccessPointGroup)
                .Include(x => x.AccessPointGroup!.Certificate)
                .SingleAsync(x =>
                x.ProjectId == ProjectId &&
                (x.PublicEndPoint == hostEndPoint || x.PrivateEndPoint == hostEndPoint));

            // update serverId associated with AccessPoint
            if (accessPoint.ServerId != serverId)
            {
                accessPoint.ServerId = serverId;
                vhContext.AccessPoints.Update(accessPoint);
                await vhContext.SaveChangesAsync();
            }

            return accessPoint.AccessPointGroup!.Certificate!.RawData;
        }

        [HttpPost("server-status")]
        public async Task SendServerStatus(Guid serverId, ServerStatus serverStatus)
        {
            // get current accessToken
            await PublicCycleHelper.UpdateCycle();

            await using VhContext vhContext = new();
            var query = from s in vhContext.Servers
                        join ssl in vhContext.ServerStatusLogs on s.ServerId equals ssl.ServerId into grouping
                        from ssl in grouping.DefaultIfEmpty()
                        where s.ServerId == serverId && ssl.IsLast
                        select new { s, ssl };
            var queryRes = await query.SingleAsync();

            // remove IsLast
            if (queryRes.ssl != null)
            {
                queryRes.ssl.IsLast = false;
                vhContext.Update(queryRes.ssl);
            }

            vhContext.ServerStatusLogs.Add(new ServerStatusLog
            {
                ServerId = serverId,
                IsSubscribe = false,
                IsLast = true,
                CreatedTime = DateTime.UtcNow,
                FreeMemory = serverStatus.FreeMemory,
                TcpConnectionCount = serverStatus.TcpConnectionCount,
                UdpConnectionCount = serverStatus.UdpConnectionCount,
                SessionCount = serverStatus.SessionCount,
                ThreadCount = serverStatus.ThreadCount
            });
            await vhContext.SaveChangesAsync();
        }

        [HttpPost("server-subscribe")]
        public async Task ServerSubscribe(Guid serverId, ServerInfo serverInfo)
        {
            await using VhContext vhContext = new();
            var server = await vhContext.Servers.FindAsync(serverId);
            var isNew = server == null;
            if (server == null)
                server = new Models.Server
                {
                    ProjectId = ProjectId,
                    ServerId = serverId,
                    CreatedTime = DateTime.UtcNow
                };
            else if (server.ProjectId != ProjectId)
                throw new AlreadyExistsException(
                    $"This serverId is used by another project! Change your server id. id: {serverId}");

            // update server
            server.EnvironmentVersion = serverInfo.EnvironmentVersion?.ToString();
            server.LocalIp = serverInfo.LocalIp;
            server.PublicIp = serverInfo.PublicIp;
            server.OsInfo = serverInfo.OsInfo;
            server.MachineName = serverInfo.MachineName;
            server.SubscribeTime = DateTime.UtcNow;
            server.TotalMemory = serverInfo.TotalMemory;
            server.Version = serverInfo.Version.ToString();

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
                CreatedTime = DateTime.UtcNow,
                IsLast = true,
                IsSubscribe = true
            };
            await vhContext.ServerStatusLogs.AddAsync(statusLog);
            await vhContext.SaveChangesAsync();
        }
    }
}
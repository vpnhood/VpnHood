using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Models;
using VpnHood.Server;
using VpnHood.AccessServer.Exceptions;
using System.Net;
using VpnHood.Common;
using VpnHood.Common.Messaging;
using VpnHood.Server.Messaging;

namespace VpnHood.AccessServer.Controllers
{
    [Route("/api/access")]
    [Authorize(AuthenticationSchemes = "auth", Roles = "Admin, VpnServer")]
    public class AccessController : SuperController<AccessController>
    {
        private Guid ProjectId
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

        private static bool ValidateRequest(SessionRequestEx sessionRequestEx, byte[] tokenSecret)
        {
            var encryptClientId = Util.EncryptClientId(sessionRequestEx.ClientInfo.ClientId, tokenSecret);
            return Enumerable.SequenceEqual(encryptClientId, sessionRequestEx.EncryptedClientId);
        }

        private static SessionResponseEx BuidSessionResponse(VhContext vhContext, Session session, AccessToken accessToken, Models.AccessUsage accessUsageDb)
        {
            // create common accessUsage
            var accessUsage = new Common.Messaging.AccessUsage()
            {
                MaxClientCount = accessToken.MaxClient,
                MaxTraffic = accessToken.MaxTraffic,
                ExpirationTime = accessUsageDb.EndTime,
                SentTraffic = accessUsageDb.CycleSentTraffic,
                ReceivedTraffic = accessUsageDb.CycleReceivedTraffic,
                ActiveClientCount = 0
            };

            // validate session status
            if (session.ErrorCode == SessionErrorCode.Ok)
            {
                // check token expiration
                if (accessUsage.ExpirationTime != null && accessUsage.ExpirationTime < DateTime.Now)
                    return new(SessionErrorCode.AccessExpired) { AccessUsage = accessUsage, ErrorMessage = "Access Expired!" };

                // check traffic
                else if (accessUsage.MaxTraffic != 0 && accessUsage.SentTraffic + accessUsage.ReceivedTraffic > accessUsage.MaxTraffic)
                    return new(SessionErrorCode.AccessTrafficOverflow) { AccessUsage = accessUsage, ErrorMessage = "All traffic quota has been consumed!" };

                var otherSessions = vhContext.Sessions
                    .Where(x => x.EndTime == null && x.AccessUsageId == session.AccessUsageId)
                    .OrderBy(x => x.CreatedTime).ToArray();

                // suppressedTo yourself
                var selfSessions = otherSessions.Where(x => x.ClientKeyId == session.ClientKeyId);
                if (selfSessions.Any())
                {
                    session.SuppressedTo = SessionSuppressType.YourSelf;
                    foreach (var selfSession in selfSessions.ToArray())
                    {
                        selfSession.SuppressedBy = SessionSuppressType.YourSelf;
                        selfSession.ErrorCode = SessionErrorCode.SessionSuppressedBy;
                        selfSession.EndTime = DateTime.Now;
                        vhContext.Sessions.Update(selfSession);
                    }
                }

                // suppressedTo others by MaxClientCount
                if (accessUsage.MaxClientCount != 0)
                {
                    var otherSessions2 = otherSessions
                        .Where(x => x.ClientKeyId != session.ClientKeyId)
                        .OrderBy(x => x.CreatedTime).ToArray();
                    for (var i = 0; i <= otherSessions.Length - accessUsage.MaxClientCount; i++)
                    {
                        var otherSession = otherSessions2[i];
                        otherSession.SuppressedBy = SessionSuppressType.Other;
                        otherSession.ErrorCode = SessionErrorCode.SessionSuppressedBy;
                        otherSession.EndTime = DateTime.Now;
                        session.SuppressedTo = SessionSuppressType.Other;
                        vhContext.Sessions.Update(otherSession);
                    }
                }

                accessUsage.ActiveClientCount = accessToken.IsPublic ? 0 : otherSessions.Count(x=>x.EndTime == null);
            }

            // build result
            return new SessionResponseEx(SessionErrorCode.Ok)
            {
                SessionId = session.SessionId,
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

            // Get accessToken and check projectId, accessToken, accessTokenGroup
            var query = from ATG in vhContext.AccessTokenGroups
                        join AT in vhContext.AccessTokens on ATG.AccessTokenGroupId equals AT.AccessTokenGroupId
                        join EP in vhContext.ServerEndPoints on ATG.AccessTokenGroupId equals EP.AccessTokenGroupId
                        where ATG.ProjectId == ProjectId && AT.AccessTokenId == sessionRequestEx.TokenId &&
                                (EP.PulicEndPoint == hostEndPoint || EP.PrivateEndPoint == hostEndPoint)
                        select new { AT, EP.ServerId, EP.ServerEndPointId };
            var result = await query.SingleAsync();
            var accessToken = result.AT;

            // validate the request
            if (!ValidateRequest(sessionRequestEx, accessToken.Secret))
                return new(SessionErrorCode.GeneralError) { ErrorMessage = "Could not validate the request!" };

            // create client or update if changed
            var client = await vhContext.Clients.SingleOrDefaultAsync(x => x.ProjectId == ProjectId && x.UserClientId == clientInfo.ClientId);
            if (client == null)
            {
                client = new Client
                {
                    ClientId = Guid.NewGuid(),
                    ProjectId = ProjectId,
                    UserClientId = clientInfo.ClientId,
                    ClientIp = clientIp?.ToString(),
                    ClientVersion = clientInfo.ClientVersion,
                    UserAgent = clientInfo.UserAgent,
                    CreatedTime = DateTime.Now,
                };
                await vhContext.Clients.AddAsync(client);
            }
            else if (client.UserAgent != clientInfo.UserAgent || client.ClientVersion != clientInfo.ClientVersion || client.ClientIp != clientIp)
            {
                client.UserAgent = clientInfo.UserAgent;
                client.ClientVersion = clientInfo.ClientVersion;
                client.ClientIp = clientIp;
                vhContext.Clients.Update(client);
            }

            // update ServerEndPoint.ServerId if changed
            if (result.ServerId != serverId)
            {
                var serverEndPoint = await vhContext.ServerEndPoints.SingleAsync(x => x.ProjectId == ProjectId && x.ServerEndPointId == result.ServerEndPointId);
                serverEndPoint.ServerId = serverId;
                vhContext.ServerEndPoints.Update(serverEndPoint); //todo test
            }

            // get or create accessUsage
            Guid? clientKeyId = accessToken.IsPublic ? client.ClientId : null;
            var accessUsageDb = await vhContext.AccessUsages.SingleOrDefaultAsync(x => x.AccessTokenId == accessToken.AccessTokenId && x.ClientKeyId == clientKeyId);
            if (accessUsageDb == null)
            {
                accessUsageDb = new Models.AccessUsage
                {
                    AccessUsageId = Guid.NewGuid(),
                    AccessTokenId = sessionRequestEx.TokenId,
                    ClientKeyId = accessToken.IsPublic ? client.ClientId : null,
                    CreatedTime = DateTime.Now,
                    ModifiedTime = DateTime.Now,
                    EndTime = accessToken.EndTime,
                };

                // set accessToken expiration time on first use
                if (accessToken.EndTime == null && accessToken.Lifetime != 0)
                {
                    accessUsageDb.EndTime = DateTime.Now.AddDays(accessToken.Lifetime); //todo test
                }

                _logger.LogInformation($"Access has been activated! AccessUsageId: {accessUsageDb.AccessUsageId}");
                await vhContext.AccessUsages.AddAsync(accessUsageDb);
            }
            else
            {
                accessUsageDb.ModifiedTime = DateTime.Now;
                vhContext.AccessUsages.Update(accessUsageDb);
            }

            // create session
            Session session = new()
            {
                SessionKey = Util.GenerateSessionKey(),
                CreatedTime = DateTime.Now,
                AccessedTime = DateTime.Now,
                AccessUsageId = accessUsageDb.AccessUsageId,
                ClientIp = clientIp,
                ClientKeyId = client.ClientId,
                ClientVersion = client.ClientVersion,
                EndTime = null,
                ServerId = serverId,
                SuppressedBy = SessionSuppressType.None,
                SuppressedTo = SessionSuppressType.None,
                ErrorCode = SessionErrorCode.Ok,
                ErrorMessage = null
            };

            var ret = BuidSessionResponse(vhContext, session, accessToken, accessUsageDb);
            if (ret.ErrorCode == SessionErrorCode.Ok)
            {
                vhContext.Sessions.Add(session);
                await vhContext.SaveChangesAsync();
                ret.SessionId = session.SessionId;
            }
            return ret;
        }

        [HttpPost("sessions/{sessionId}")]
        public async Task<SessionResponseEx> Session_Get(Guid serverId, uint sessionId, string hostEndPoint, IPAddress? clientIp)
        {
            _ = clientIp;
            _ = serverId;
            hostEndPoint = AccessUtil.ValidateIpEndPoint(hostEndPoint);
            await using VhContext vhContext = new();

            // make sure hostEndPoint is accessibe by this session
            var query = from ATG in vhContext.AccessTokenGroups
                        join AT in vhContext.AccessTokens on ATG.AccessTokenGroupId equals AT.AccessTokenGroupId
                        join AU in vhContext.AccessUsages on AT.AccessTokenId equals AU.AccessTokenId
                        join S in vhContext.Sessions on AU.AccessUsageId equals S.AccessUsageId
                        join EP in vhContext.ServerEndPoints on ATG.AccessTokenGroupId equals EP.AccessTokenGroupId
                        where AT.ProjectId == ProjectId && S.SessionId == sessionId && AU.AccessUsageId == S.AccessUsageId &&
                                (EP.PulicEndPoint == hostEndPoint || EP.PrivateEndPoint == hostEndPoint)
                        select new { AT, AU, S };
            var result = await query.SingleAsync();
            
            var accessToken = result.AT;
            var accessUsage = result.AU;
            var session = result.S;

            // build response
            var ret = BuidSessionResponse(vhContext, session, accessToken, accessUsage);

            // update session AccessedTime
            result.S.AccessedTime = DateTime.Now;
            vhContext.Sessions.Update(session);
            await vhContext.SaveChangesAsync();

            return ret;
        }

        [HttpPost("sessions/{sessionId}/usage")]
        public async Task<ResponseBase> Session_AddUsage(Guid serverId, uint sessionId, UsageInfo usageInfo, bool closeSession = false)
        {
            await using VhContext vhContext = new();

            // make sure hostEndPoint is accessibe by this session
            var query = from AT in vhContext.AccessTokens
                        join AU in vhContext.AccessUsages on AT.AccessTokenId equals AU.AccessTokenId
                        join S in vhContext.Sessions on AU.AccessUsageId equals S.AccessUsageId
                        where AT.ProjectId == ProjectId && S.SessionId == sessionId && AU.AccessUsageId == S.AccessUsageId
                        select new { AT, AU, S };
            var result = await query.SingleAsync();

            var accessToken = result.AT;
            var accessUsage = result.AU;
            var session = result.S;

            // add usage 
            _logger.LogInformation($"AddUsage to {accessUsage.AccessUsageId}, SentTraffic: {usageInfo.SentTraffic / 1000000} MB, ReceivedTraffic: {usageInfo.ReceivedTraffic / 1000000} MB");
            accessUsage.CycleSentTraffic += usageInfo.SentTraffic;
            accessUsage.CycleReceivedTraffic += usageInfo.ReceivedTraffic;
            accessUsage.TotalSentTraffic += usageInfo.SentTraffic;
            accessUsage.TotalReceivedTraffic += usageInfo.ReceivedTraffic;
            accessUsage.ModifiedTime = DateTime.Now;
            vhContext.AccessUsages.Update(accessUsage);

            // insert AccessUsageLog
            await vhContext.AccessUsageLogs.AddAsync(new AccessUsageLog()
            {
                SessionId = session.SessionId,
                ReceivedTraffic = usageInfo.ReceivedTraffic,
                SentTraffic = usageInfo.SentTraffic,
                CycleReceivedTraffic = accessUsage.CycleReceivedTraffic,
                CycleSentTraffic = accessUsage.CycleSentTraffic,
                TotalReceivedTraffic = accessUsage.TotalReceivedTraffic,
                TotalSentTraffic = accessUsage.TotalSentTraffic,
                ServerId = serverId,
                CreatedTime = DateTime.Now
            });

            // update accessdTime

            // build response
            var ret = BuidSessionResponse(vhContext, session, accessToken, accessUsage);

            // close session
            if (closeSession && ret.ErrorCode == SessionErrorCode.Ok)
            {
                session.ErrorCode = SessionErrorCode.SessionClosed;
                session.EndTime = DateTime.Now;
            }
            
            // update session
            session.AccessedTime = DateTime.Now;
            vhContext.Sessions.Update(session);

            await vhContext.SaveChangesAsync();
            return new ResponseBase(ret);

        }

        [HttpGet("ssl-certificates/{requestEndPoint}")]
        public async Task<byte[]> GetSslCertificateData(Guid serverId, string requestEndPoint)
        {
            await using VhContext vhContext = new();
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

        [HttpPost("server-status")]
        public async Task SendServerStatus(Guid serverId, ServerStatus serverStatus)
        {
            // get current accessToken
            await PublicCycleHelper.UpdateCycle();

            await using VhContext vhContext = new();
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
                TcpConnectionCount = serverStatus.TcpConnectionCount,
                UdpConnectionCount = serverStatus.UdpConnectionCount,
                SessionCount = serverStatus.SessionCount,
                ThreadCount = serverStatus.ThreadCount,
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
            server.OsInfo = serverInfo.OsInfo;
            server.MachineName = serverInfo.MachineName;
            server.SubscribeTime = DateTime.Now;
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
                CreatedTime = DateTime.Now,
                IsLast = true,
                IsSubscribe = true
            };
            await vhContext.ServerStatusLogs.AddAsync(statusLog);
            await vhContext.SaveChangesAsync();
        }
    }
}

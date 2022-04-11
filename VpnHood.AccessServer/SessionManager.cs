using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Caching;
using VpnHood.AccessServer.Models;
using VpnHood.Common;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
using VpnHood.Common.Trackers;
using VpnHood.Server.Messaging;

namespace VpnHood.AccessServer;

public class SessionManager
{
    private readonly ILogger<SessionManager> _logger;
    private readonly ServerManager _serverManager;
    private readonly SystemCache _systemCache;

    public SessionManager(
        ILogger<SessionManager> logger, 
        ServerManager serverManager,
        SystemCache systemCache
        )
    {
        _logger = logger;
        _serverManager = serverManager;
        _systemCache = systemCache;
    }
    
    private static bool ValidateRequest(SessionRequest sessionRequest, byte[] tokenSecret)
    {
        var encryptClientId = Util.EncryptClientId(sessionRequest.ClientInfo.ClientId, tokenSecret);
        return encryptClientId.SequenceEqual(sessionRequest.EncryptedClientId);
    }

    public async Task<SessionResponseEx> Create(SessionRequestEx sessionRequestEx, VhContext vhContext, Models.Server server)
    {
        // validate argument
        if (server.AccessPoints == null) throw new ArgumentException("AccessPoints is not loaded for this model.", nameof(server));

        // extract required data
        var projectId = server.ProjectId;
        var serverId = server.ServerId;
        var clientIp = sessionRequestEx.ClientIp;
        var clientInfo = sessionRequestEx.ClientInfo;
        var requestEndPoint = sessionRequestEx.HostEndPoint;
        var anyIp = requestEndPoint.AddressFamily == AddressFamily.InterNetworkV6
            ? IPAddress.IPv6Any
            : IPAddress.Any;

        // Get accessToken and check projectId
        var accessToken = await vhContext.AccessTokens
            .Include(x=>x.AccessPointGroup)
            .SingleAsync(x=>x.AccessTokenId == sessionRequestEx.TokenId && x.ProjectId==projectId);

        // validate the request
        if (!ValidateRequest(sessionRequestEx, accessToken.Secret))
            return new SessionResponseEx(SessionErrorCode.GeneralError)
            {
                ErrorMessage = "Could not validate the request!"
            };

        // validate request to this server
        var isValidEndPoint = server.AccessPoints.Any(x =>
            x.IsListen &&
            x.TcpPort == requestEndPoint.Port &&
            x.AccessPointGroupId == accessToken.AccessPointGroupId &&
            (x.IpAddress == anyIp.ToString() || x.IpAddress == requestEndPoint.Address.ToString())
        );
        if (!isValidEndPoint)
            return new SessionResponseEx(SessionErrorCode.GeneralError)
            {
                ErrorMessage = "Invalid EndPoint request!"
            };

        // check has Ip Locked
        if (clientIp != null && await vhContext.IpLocks.AnyAsync(x => x.ProjectId == projectId && x.IpAddress == clientIp.ToString() && x.LockedTime != null))
            return new SessionResponseEx(SessionErrorCode.AccessLocked)
            {
                ErrorMessage = "Your access has been locked! Please contact the support."
            };

        // create client or update if changed
        var clientIpToStore = clientIp != null ? IPAddressUtil.Anonymize(clientIp).ToString() : null;
        var device = await vhContext.Devices.SingleOrDefaultAsync(x => x.ProjectId == projectId && x.ClientId == clientInfo.ClientId);
        if (device == null)
        {
            device = new Device
            {
                DeviceId = Guid.NewGuid(),
                ProjectId = projectId,
                ClientId = clientInfo.ClientId,
                IpAddress = clientIpToStore,
                ClientVersion = clientInfo.ClientVersion,
                UserAgent = clientInfo.UserAgent,
                CreatedTime = DateTime.UtcNow,
                ModifiedTime = DateTime.UtcNow
            };
            await vhContext.Devices.AddAsync(device);
        }
        else
        {
            device.UserAgent = clientInfo.UserAgent;
            device.ClientVersion = clientInfo.ClientVersion;
            device.ModifiedTime = DateTime.UtcNow;
            device.IpAddress = clientIpToStore;
        }

        // check has device Locked
        if (device.LockedTime != null)
            return new SessionResponseEx(SessionErrorCode.AccessLocked)
            {
                ErrorMessage = "Your access has been locked! Please contact the support."
            };


        // get or create access
        Guid? deviceId = accessToken.IsPublic ? device.DeviceId : null;
        var access = await vhContext.Accesses.SingleOrDefaultAsync(x => x.AccessTokenId == accessToken.AccessTokenId && x.DeviceId == deviceId);

        // Update or Create Access
        var isNewAccess = access == null;
        access ??= new Access
        {
            AccessId = Guid.NewGuid(),
            AccessTokenId = sessionRequestEx.TokenId,
            DeviceId = accessToken.IsPublic ? device.DeviceId : null,
            CreatedTime = DateTime.UtcNow,
            EndTime = accessToken.EndTime,
        };

        // set access time
        access.AccessedTime = DateTime.UtcNow;

        // set accessToken expiration time on first use
        if (access.EndTime == null && accessToken.Lifetime != 0)
            access.EndTime = DateTime.UtcNow.AddDays(accessToken.Lifetime);

        if (isNewAccess)
        {
            _logger.LogInformation($"Access has been activated! AccessId: {access.AccessId}");
            await vhContext.Accesses.AddAsync(access);
        }

        // create session
        var session = new Session
        {
            SessionKey = Util.GenerateSessionKey(),
            CreatedTime = DateTime.UtcNow,
            AccessedTime = DateTime.UtcNow,
            AccessTokenId = accessToken.AccessTokenId,
            AccessId = access.AccessId,
            DeviceIp = clientIpToStore,
            DeviceId = device.DeviceId,
            ClientVersion = device.ClientVersion,
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

        // check supported version
        var minSupportedVersion = Version.Parse("2.3.289");
        if (string.IsNullOrEmpty(clientInfo.ClientVersion) || Version.Parse(clientInfo.ClientVersion).CompareTo(minSupportedVersion) < 0)
            return new SessionResponseEx(SessionErrorCode.UnsupportedClient) { ErrorMessage = "This version is not supported! You need to update your app." };

        // Check Redirect to another server if everything was ok
        var bestEndPoint = await _serverManager.FindBestServerForDevice(vhContext, server, requestEndPoint, accessToken.AccessPointGroupId, device.DeviceId);
        if (bestEndPoint == null)
            return new SessionResponseEx(SessionErrorCode.GeneralError) { ErrorMessage = "Could not find any free server!" };

        if (!bestEndPoint.Equals(requestEndPoint))
            return new SessionResponseEx(SessionErrorCode.RedirectHost) { RedirectHostEndPoint = bestEndPoint };

        // Add session
        session = (await vhContext.Sessions.AddAsync(session)).Entity;

        await using var transaction = await vhContext.Database.BeginTransactionAsync();
        await vhContext.SaveChangesAsync();

        // insert AccessUsageLog
        await vhContext.AccessUsages.AddAsync(new AccessUsageEx
        {
            AccessId = session.AccessId,
            SessionId = (uint)session.SessionId,
            ReceivedTraffic = 0,
            SentTraffic = 0,
            ProjectId = projectId,
            AccessPointGroupId = accessToken.AccessPointGroupId,
            AccessTokenId = accessToken.AccessTokenId,
            DeviceId = device.DeviceId,
            CycleReceivedTraffic = access.CycleReceivedTraffic,
            CycleSentTraffic = access.CycleSentTraffic,
            TotalReceivedTraffic = access.TotalReceivedTraffic,
            TotalSentTraffic = access.TotalSentTraffic,
            CreatedTime = access.AccessedTime,
            ServerId = serverId
        });

        await vhContext.SaveChangesAsync();
        await vhContext.Database.CommitTransactionAsync();

        _ = TrackSession(device, accessToken.AccessPointGroup!.AccessPointGroupName ?? "farm-" + accessToken.AccessPointGroupId, accessToken.AccessTokenName ?? "token-" + accessToken.AccessTokenId);
        ret.SessionId = (uint)session.SessionId;
        return ret;
    }

    private static async Task TrackSession(Device device, string farmName, string accessTokenName)
    {
        if (device.ProjectId != Guid.Parse("8b90f69b-264f-4d4f-9d42-f614de4e3aea"))
            return;

        var analyticsTracker = new GoogleAnalyticsTracker("UA-183010362-2", device.DeviceId.ToString(),
            "VpnHoodService", device.ClientVersion ?? "1", device.UserAgent)
        {
            IpAddress = device.IpAddress != null && IPAddress.TryParse(device.IpAddress, out var ip) ? ip : null,
        };

        var trackData = new TrackData($"{farmName}/{accessTokenName}", accessTokenName);
        await analyticsTracker.Track(trackData);
    }

    private SessionResponseEx BuildSessionResponse(VhContext vhContext, Session session, AccessToken accessToken, Access access)
    {
        // create common accessUsage
        var accessUsage2 = new AccessUsage
        {
            MaxClientCount = accessToken.MaxDevice,
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
}
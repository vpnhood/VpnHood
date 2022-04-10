using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Caching;
using VpnHood.AccessServer.Models;
using VpnHood.Common;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
using VpnHood.Server.Messaging;

namespace VpnHood.AccessServer;

public class SessionManager
{
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<SessionManager> _logger;
    private readonly ServerManager _serverManager;
    private ConcurrentDictionary<long, SessionCache> _sessions = new();
    private ConcurrentDictionary<Guid, AccessCache> _accesses = new();

    public SessionManager(IMemoryCache memoryCache, ILogger<SessionManager> logger, ServerManager serverManager)
    {
        _memoryCache = memoryCache;
        _logger = logger;
        _serverManager = serverManager;
    }

    public async Task Sync(VhContext vhContext)
    {
        // update 
        var accesses = new List<Access>();
        foreach (var item in _accesses)
        {
            var access = new Access()
            {
                AccessId = item.Key,
                AccessedTime = DateTime.UtcNow
            };
        }
        // update database
    }

    private static bool ValidateRequest(SessionRequest sessionRequest, byte[] tokenSecret)
    {
        var encryptClientId = Util.EncryptClientId(sessionRequest.ClientInfo.ClientId, tokenSecret);
        return encryptClientId.SequenceEqual(sessionRequest.EncryptedClientId);
    }

    public async Task<SessionResponseEx> Create(SessionRequestEx sessionRequestEx, VhContext vhContext, Guid projectId, Guid serverId)
    {
        var clientIp = sessionRequestEx.ClientIp;
        var clientInfo = sessionRequestEx.ClientInfo;
        var requestEndPoint = sessionRequestEx.HostEndPoint;
        var anyIp = requestEndPoint.AddressFamily == AddressFamily.InterNetworkV6
            ? IPAddress.IPv6Any
            : IPAddress.Any;

        // Get accessToken and check projectId, accessToken
        var query = from accessPointGroup in vhContext.AccessPointGroups
            join at in vhContext.AccessTokens on accessPointGroup.AccessPointGroupId equals at.AccessPointGroupId
            join accessPoint in vhContext.AccessPoints on accessPointGroup.AccessPointGroupId equals accessPoint.AccessPointGroupId
            where accessPointGroup.ProjectId == projectId &&
                  accessPoint.ServerId == serverId &&
                  at.AccessTokenId == sessionRequestEx.TokenId &&
                  accessPoint.IsListen &&
                  accessPoint.TcpPort == requestEndPoint.Port &&
                  (accessPoint.IpAddress == anyIp.ToString() || accessPoint.IpAddress == requestEndPoint.Address.ToString())
            select new { acToken = at, accessPoint.ServerId, accessPointGroup.AccessPointGroupName };

        var result = await query.SingleAsync();
        var accessToken = result.acToken;

        // validate the request
        if (!ValidateRequest(sessionRequestEx, accessToken.Secret))
            return new SessionResponseEx(SessionErrorCode.GeneralError)
            {
                ErrorMessage = "Could not validate the request!"
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
        Models.Server? server = null; //todo
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

        _ = TrackSession(device, result.AccessPointGroupName ?? "farm-" + accessToken.AccessPointGroupId, accessToken.AccessTokenName ?? "token-" + accessToken.AccessTokenId);
        ret.SessionId = (uint)session.SessionId;
        return ret;
    }

    private object TrackSession(Device device, string accessTokenAccessPointGroupId, string accessTokenAccessTokenName)
    {
        throw new NotImplementedException();
    }

    private SessionResponseEx BuildSessionResponse(VhContext vhContext, Session session, AccessToken accessToken, Access access)
    {
        throw new NotImplementedException();
    }
}
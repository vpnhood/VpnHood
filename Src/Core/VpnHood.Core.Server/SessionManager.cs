using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Trackers;
using VpnHood.Core.Packets;
using VpnHood.Core.Server.Abstractions;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Core.Server.Access.Managers;
using VpnHood.Core.Server.Access.Messaging;
using VpnHood.Core.Server.Exceptions;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.Sockets;
using VpnHood.Core.Tunneling.Utils;
using VpnHood.Core.VpnAdapters.Abstractions;

namespace VpnHood.Core.Server;

public class SessionManager : IAsyncDisposable, IDisposable
{
    private bool _disposed;
    private readonly IAccessManager _accessManager;
    private readonly ISocketFactory _socketFactory;
    private readonly IVpnAdapter? _vpnAdapter;
    private readonly TimeSpan _deadSessionTimeout;
    private readonly Job _heartbeatJob;
    private readonly SessionLocalService _sessionLocalService;
    private readonly VirtualIpManager _virtualIpManager;
    private byte[] _serverSecret;

    public string ApiKey { get; private set; }
    public INetFilter NetFilter { get; }
    public Version ServerVersion { get; }
    public ConcurrentDictionary<ulong, Session> Sessions { get; } = new();
    public TrackingOptions TrackingOptions { get; set; } = new();
    public SessionOptions SessionOptions { get; set; } = new();
    public ITracker? Tracker { get; }
    public bool IsVpnAdapterSupported => _vpnAdapter != null;
    public IpNetwork VirtualIpNetworkV4 => _virtualIpManager.IpNetworkV4;
    public IpNetwork VirtualIpNetworkV6 => _virtualIpManager.IpNetworkV6;

    public byte[] ServerSecret {
        get => _serverSecret;
        set {
            ApiKey = HttpUtil.GetApiKey(value, TunnelDefaults.HttpPassCheck);
            _serverSecret = value;
        }
    }

    internal SessionManager(
        IAccessManager accessManager,
        INetFilter netFilter,
        ISocketFactory socketFactory,
        ITracker? tracker,
        IVpnAdapter? vpnAdapter,
        Version serverVersion,
        string storagePath,
        SessionManagerOptions options)
    {
        _accessManager = accessManager ?? throw new ArgumentNullException(nameof(accessManager));
        _socketFactory = socketFactory ?? throw new ArgumentNullException(nameof(socketFactory));
        _vpnAdapter = vpnAdapter;
        _serverSecret = VhUtils.GenerateKey(128);
        _deadSessionTimeout = options.DeadSessionTimeout;
        _sessionLocalService = new SessionLocalService(Path.Combine(storagePath, "sessions"));
        _virtualIpManager = new VirtualIpManager(options.VirtualIpNetworkV4, options.VirtualIpNetworkV6,
            Path.Combine(storagePath, "last-virtual-ips.json"));

        Tracker = tracker;
        ApiKey = HttpUtil.GetApiKey(_serverSecret, TunnelDefaults.HttpPassCheck);
        NetFilter = netFilter;
        ServerVersion = serverVersion;
        if (_vpnAdapter != null)
            _vpnAdapter.PacketReceived += VpnAdapter_PacketReceived;

        _heartbeatJob = new Job(SendHeartbeat, options.HeartbeatInterval, "Heartbeat");
    }

    private Session CreateSessionInternal(
        SessionResponseEx sessionResponseEx,
        IPEndPointPair ipEndPointPair,
        string requestId,
        bool isRecovery)
    {
        // add to sessions
        var session = BuildSessionFromResponseEx(sessionResponseEx, isRecovery: isRecovery);
        if (Sessions.TryAdd(session.SessionId, session)) {
            _sessionLocalService.Update(session);
            return session;
        }

        session.SessionResponseEx.ErrorMessage = "Could not add session to collection.";
        session.SessionResponseEx.ErrorCode = SessionErrorCode.SessionError;
        session.Dispose();
        throw new ServerSessionException(ipEndPointPair.RemoteEndPoint, session, session.SessionResponseEx, requestId);
    }

    private Session BuildSessionFromResponseEx(SessionResponseEx sessionResponseEx, bool isRecovery)
    {
        var extraData = sessionResponseEx.ExtraData != null
            ? JsonUtils.Deserialize<SessionExtraData>(sessionResponseEx.ExtraData)
            : new SessionExtraData();

        // make sure that not to give same IP to multiple sessions
        var virtualIps = isRecovery
            ? _sessionLocalService.Get(sessionResponseEx.SessionId).VirtualIps
            : _virtualIpManager.Allocate();

        // create the session
        var session = new Session(
            accessManager: _accessManager,
            vpnAdapter: _vpnAdapter,
            netFilter: NetFilter,
            socketFactory: _socketFactory,
            options: SessionOptions,
            trackingOptions: TrackingOptions,
            sessionResponseEx: sessionResponseEx,
            extraData: extraData,
            virtualIps: virtualIps);

        // add to virtual IPs
        _virtualIpManager.Add(virtualIps, session);
        return session;
    }

    public async Task<SessionResponseEx> CreateSession(HelloRequest helloRequest, IPEndPointPair ipEndPointPair,
        int protocolVersion)
    {
        // validate the token
        VhLogger.Instance.LogDebug("Validating the request by the access server. TokenId: {TokenId}",
            VhLogger.FormatId(helloRequest.TokenId));

        var extraData = JsonSerializer.Serialize(new SessionExtraData());
        var sessionResponseEx = await _accessManager.Session_Create(new SessionRequestEx {
            HostEndPoint = ipEndPointPair.LocalEndPoint,
            ClientIp = ipEndPointPair.RemoteEndPoint.Address,
            ExtraData = extraData,
            ClientInfo = helloRequest.ClientInfo,
            EncryptedClientId = helloRequest.EncryptedClientId,
            TokenId = helloRequest.TokenId,
            ServerLocation = helloRequest.ServerLocation,
            PlanId = helloRequest.PlanId,
            AllowRedirect = helloRequest.AllowRedirect,
            IsIpV6Supported = helloRequest.IsIpV6Supported,
            AccessCode = helloRequest.AccessCode,
            ProtocolVersion = protocolVersion
        }).VhConfigureAwait();

        // Access Error should not pass to the client in create session
        if (sessionResponseEx.ErrorCode is SessionErrorCode.AccessError)
            throw new ServerUnauthorizedAccessException(sessionResponseEx.ErrorMessage ?? "Access Error.",
                ipEndPointPair, helloRequest);

        if (sessionResponseEx.ErrorCode != SessionErrorCode.Ok)
            throw new ServerSessionException(ipEndPointPair.RemoteEndPoint, sessionResponseEx, helloRequest);

        // create the session and add it to list
        var session = CreateSessionInternal(sessionResponseEx, ipEndPointPair,
            helloRequest.RequestId, isRecovery: false);

        // Anonymous Report to GA
        _ = TryTrackNewSession(helloRequest.ClientInfo);

        VhLogger.Instance.LogInformation(GeneralEventId.Session,
            "New session has been created. SessionId: {SessionId}", VhLogger.FormatSessionId(session.SessionId));
        Report();

        return sessionResponseEx;
    }

    public void Report()
    {
        var totalCount = Sessions.Count;
        var idleCount = GetIdleSessions().Length;
        var deadCount = GetDeadSessions().Length;
        var activeCount = totalCount - idleCount - deadCount;

        VhLogger.Instance.LogInformation(
            "Sessions Status. " +
            "ActiveSessions: {ActiveSessions}, IdleSessions: {idleCount}, DeadSessions: {DeadSessions}, TotalSessions: {TotalSessions}, " +
            "TotalTcpChannels: {TotalTcpChannels}, TotalUdpChannels: {TotalUdpChannels}",
            activeCount, idleCount, deadCount, totalCount,
            Sessions.Sum(x => x.Value.TcpChannelCount), Sessions.Sum(x => x.Value.UdpConnectionCount));
    }

    private Task<bool> TryTrackNewSession(ClientInfo clientInfo)
    {
        if (Tracker == null)
            return Task.FromResult(false);

        // track new session
        var serverVersion = ServerVersion.ToString(3);
        return Tracker.TryTrack([
            new TrackEvent {
                EventName = TrackEventNames.PageView,
                Parameters = new Dictionary<string, object> {
                    { "client_version", clientInfo.ClientVersion },
                    { "server_version", serverVersion },
                    { TrackParameterNames.PageTitle, $"server_version/{serverVersion}" },
                    { TrackParameterNames.PageLocation, $"server_version/{serverVersion}" }
                }
            }
        ]);
    }

    public async Task RecoverSessions()
    {
        // recover all sessions
        var responseExs = await _accessManager.Session_GetAll().VhConfigureAwait();
        foreach (var responseEx in responseExs) {
            try {
                var session = BuildSessionFromResponseEx(responseEx, true);
                if (!Sessions.TryAdd(session.SessionId, session)) {
                    session.Dispose();
                    throw new Exception("Could not add session to collection.");
                }
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Could not add a session. SessionId: {SessionId}",
                    VhLogger.FormatSessionId(responseEx.SessionId));
            }
        }
    }

    private async Task<Session> RecoverSession(RequestBase sessionRequest, IPEndPointPair ipEndPointPair)
    {
        using var recoverLock =
            await AsyncLock.LockAsync($"Recover_session_{sessionRequest.SessionId}").VhConfigureAwait();
        var session = GetSessionById(sessionRequest.SessionId);
        if (session != null)
            return session;

        // Get session from the access server
        VhLogger.Instance.LogDebug(GeneralEventId.Session,
            "Trying to recover a session from the access server. SessionId: {SessionId}",
            VhLogger.FormatSessionId(sessionRequest.SessionId));

        try {
            // check if the session exists in the local storage
            if (_sessionLocalService.Find(sessionRequest.SessionId) == null)
                throw new ServerSessionException(ipEndPointPair.RemoteEndPoint, new SessionResponse {
                    ErrorCode = SessionErrorCode.AccessError,
                    ErrorMessage = "Invalid SessionId."
                }, sessionRequest);

            // get the session from the access server
            var sessionResponse = await _accessManager.Session_Get(sessionRequest.SessionId,
                    ipEndPointPair.LocalEndPoint, ipEndPointPair.RemoteEndPoint.Address)
                .VhConfigureAwait();

            // Check session key for recovery
            if (!sessionRequest.SessionKey.SequenceEqual(sessionResponse.SessionKey))
                throw new ServerSessionException(ipEndPointPair.RemoteEndPoint, new SessionResponse {
                    ErrorCode = SessionErrorCode.AccessError,
                    ErrorMessage = "Invalid SessionKey."
                }, sessionRequest);

            // session is authorized, so we can pass the error to client
            if (sessionResponse.ErrorCode != SessionErrorCode.Ok)
                throw new ServerSessionException(ipEndPointPair.RemoteEndPoint, sessionResponse, sessionRequest);

            // create the session
            session = CreateSessionInternal(sessionResponse, ipEndPointPair, "recovery", isRecovery: true);
            VhLogger.Instance.LogInformation(GeneralEventId.Session,
                "Session has been recovered. SessionId: {SessionId}",
                VhLogger.FormatSessionId(sessionRequest.SessionId));

            return session;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogInformation(GeneralEventId.Session, ex,
                "Could not recover a session. SessionId: {SessionId}",
                VhLogger.FormatSessionId(sessionRequest.SessionId));

            // this will block all other session recovery at early stage
            _sessionLocalService.Remove(sessionRequest.SessionId);
            throw;
        }
    }

    internal async Task<Session> GetSession(RequestBase requestBase, IPEndPointPair ipEndPointPair)
    {
        //get session
        var session = GetSessionById(requestBase.SessionId);
        if (session != null) {
            if (!requestBase.SessionKey.SequenceEqual(session.SessionKey))
                throw new ServerUnauthorizedAccessException("Invalid session key.", ipEndPointPair, session);
        }
        // try to restore session if not found
        else {
            session = await RecoverSession(requestBase, ipEndPointPair).VhConfigureAwait();
        }

        if (session.SessionResponseEx.ErrorCode != SessionErrorCode.Ok)
            throw new ServerSessionException(ipEndPointPair.RemoteEndPoint, session, session.SessionResponseEx,
                requestBase.RequestId);

        // unexpected close (disposed without error code)
        if (session.IsDisposed)
            throw new ServerSessionException(ipEndPointPair.RemoteEndPoint, session,
                new SessionResponse {
                    ErrorCode = SessionErrorCode.SessionClosed,
                    ErrorMessage = session.SessionResponseEx.ErrorMessage,
                    AccessUsage = session.SessionResponseEx.AccessUsage,
                    SuppressedBy = session.SessionResponseEx.SuppressedBy,
                    RedirectHostEndPoint = session.SessionResponseEx.RedirectHostEndPoint
                },
                requestBase.RequestId);

        return session;
    }

    private async ValueTask SendHeartbeat(CancellationToken cancellationToken)
    {
        if (Tracker == null)
            return;

        await Tracker.Track(new TrackEvent {
            EventName = "heartbeat",
            Parameters = new Dictionary<string, object> {
                { "session_count", Sessions.Count(x => !x.Value.IsDisposed) }
            }
        }, cancellationToken);
    }

    private Session[] GetIdleSessions()
    {
        var minSessionActivityTime = FastDateTime.Now - SessionOptions.TimeoutValue;
        return Sessions
            .Values
            .Where(x =>
                !x.IsDisposed &&
                !x.IsSyncRequired &&
                x.LastActivityTime < minSessionActivityTime)
            .ToArray(); // make sure make a copy to avoid modification in the loop
    }

    private Session[] GetFailedSessions()
    {
        return Sessions
            .Values
            .Where(x =>
                !x.IsDisposed &&
                !x.IsSyncRequired &&
                x.SessionResponseEx.ErrorCode != SessionErrorCode.Ok)
            .ToArray();
    }

    private Session[] GetDeadSessions()
    {
        var utcNow = DateTime.UtcNow;
        return Sessions
            .Values
            .Where(x =>
                x.IsDisposed &&
                !x.IsSyncRequired &&
                utcNow - x.DisposedTime > _deadSessionTimeout)
            .ToArray(); // make sure make a copy to avoid modification in the loop
    }

    // remove sessions from memory that are idle but not disposed yet
    private void RemoveIdleSessions()
    {
        var idleSessions = GetIdleSessions();
        if (idleSessions.Length == 0)
            return;

        // 
        var notSyncedIdleSessions = idleSessions.Where(x => x.Traffic.Total > 0).ToArray();
        if (notSyncedIdleSessions.Length >0 )
            VhLogger.Instance.LogDebug(GeneralEventId.Session, "Syncing {IdleSessions} idle sessions...", notSyncedIdleSessions.Length);

        var syncedIdleSessions = idleSessions.Where(x => x.Traffic.Total == 0).ToArray();
        if (syncedIdleSessions.Length > 0) {
            VhLogger.Instance.LogDebug(GeneralEventId.Session, "Removing {IdleSessions} idle sessions...", syncedIdleSessions.Length);
            RemoveSessions(syncedIdleSessions);
        }
    }
    private void DisposeFailedSessions()
    {
        var failedSessions = GetFailedSessions();
        if (failedSessions.Length == 0)
            return;

        VhLogger.Instance.LogDebug(GeneralEventId.Session, "Disposing {FailedSessions} failed sessions...", failedSessions.Length);
        foreach (var failedSession in failedSessions)
            failedSession.Dispose();
    }

    private void DisposeExpiredSessions()
    {
        var utcNow = DateTime.UtcNow;
        var expiredSessions = Sessions.Values
            .Where(x => !x.IsDisposed && x.SessionResponseEx.AccessUsage?.ExpirationTime < utcNow)
            .ToArray();

        if (expiredSessions.Length == 0)
            return;

        VhLogger.Instance.LogDebug(GeneralEventId.Session, "Disposing {ExpiredSessions} expired sessions...", expiredSessions.Length);
        foreach (var session in expiredSessions) {
            session.SessionResponseEx = new SessionResponse {
                ErrorCode = SessionErrorCode.AccessExpired
            };
            session.Dispose();
        }
    }

    // remove sessions that are disposed a long time
    private void RemoveDisposedSessions()
    {
        var deadSessions = GetDeadSessions();
        if (deadSessions.Length == 0)
            return;

        VhLogger.Instance.LogDebug(GeneralEventId.Session, "Removing {DeadSessions} disposed sessions...", deadSessions.Length);
        RemoveSessions(deadSessions);
    }

    public void RemoveSessions(Session[] sessions)
    {
        foreach (var session in sessions)
            RemoveSession(session);
    }

    public void RemoveSession(Session session)
    {
        session.Dispose();
        Sessions.TryRemove(session.SessionId, out _);
        _sessionLocalService.Update(session); // let update the last state
        _virtualIpManager.Release(session.VirtualIps);
    }

    public Session? GetSessionById(ulong sessionId)
    {
        Sessions.TryGetValue(sessionId, out var session);
        return session;
    }

    private SessionUsage[] _pendingUsages = [];

    public SessionUsage[] CollectSessionUsages(bool force = false)
    {
        // traffic should be collected if there is some traffic and last activity time is expired
        // it makes sure that we notify the manager that session was still active
        var minActivityTime = FastDateTime.Now - SessionOptions.SyncIntervalValue;

        // get all sessions and their traffic
        var usages = Sessions.Values
            .Where(x =>
                (x.Traffic.Total > 0 && force) ||
                (x.Traffic.Total > 0 && x.LastActivityTime < minActivityTime) ||
                x.Traffic.Total >= SessionOptions.SyncCacheSizeValue ||
                x.ResetSyncRequired())
            .Select(x => {
                var traffic = x.ResetTraffic();
                return new SessionUsage {
                    SessionId = x.SessionId,
                    Received = traffic.Received,
                    Sent = traffic.Sent,
                    Closed = x.SessionResponseEx.ErrorCode == SessionErrorCode.SessionClosed
                };
            })
            .ToArray();

        // merge to usage list if there is some data from last sync
        var pendingUsages = _pendingUsages.ToDictionary(x => x.SessionId);
        foreach (var usage in usages) {
            if (pendingUsages.TryGetValue(usage.SessionId, out var pendingUsage)) {
                usage.Received += pendingUsage.Received;
                usage.Sent += pendingUsage.Sent;
                usage.Closed |= pendingUsage.Closed;
            }
        }

        _pendingUsages = usages;
        return usages;
    }

    public void ApplySessionResponses(Dictionary<ulong, SessionResponse> sessionResponses)
    {
        // update sessions from the result of access manager
        var failedSessionResponsePairs = sessionResponses
            .Where(x => x.Value.ErrorCode != SessionErrorCode.Ok)
            .ToArray();

        if (failedSessionResponsePairs.Length > 0) {
            VhLogger.Instance.LogInformation(GeneralEventId.Session, 
                "Set error responses from Access Manager to {FailedSessions} sessions.", 
                failedSessionResponsePairs.Length);

            foreach (var responsePair in failedSessionResponsePairs) {
                if (Sessions.TryGetValue(responsePair.Key, out var session)) {
                    VhLogger.Instance.LogDebug(GeneralEventId.Session,
                        "Set Access Manager error response to a session. SessionId: {SessionId}, ErrorCode: {ErrorCode}",
                        responsePair.Key, responsePair.Value.ErrorCode);

                    session.SessionResponseEx = responsePair.Value;
                }
            }
        }

        // clear usage if sent successfully
        _pendingUsages = [];

        // cleanup sessions that are not in the response
        Cleanup();
    }

    private void Cleanup()
    {
        RemoveIdleSessions(); // dispose idle sessions
        DisposeExpiredSessions(); // dispose expired sessions
        DisposeFailedSessions(); // dispose failed sessions
        RemoveDisposedSessions(); // remove dead sessions
    }

    private readonly AsyncLock _syncLock = new();
    public async Task Sync(bool force = false)
    {
        using var lockResult = await _syncLock.LockAsync(TimeSpan.Zero).VhConfigureAwait();
        if (!lockResult.Succeeded) 
            return;

        var sessionUsages = CollectSessionUsages(force);
        var sessionResponses = await _accessManager.Session_AddUsages(sessionUsages).VhConfigureAwait();
        ApplySessionResponses(sessionResponses);
    }

    public async Task CloseSession(ulong sessionId)
    {
        var session = GetSessionById(sessionId)
                      ?? throw new KeyNotFoundException($"Could not find Session. SessionId: {sessionId}");

        // immediately close the session from the access server, to prevent get SuppressByYourself error
        session.SessionResponseEx.ErrorCode = SessionErrorCode.SessionClosed;
        session.SetSyncRequired();
        await Sync();

        // remove after sync to make sure it is not added by the sync
        _sessionLocalService.Remove(session.SessionId);
    }

    private void VpnAdapter_PacketReceived(object sender, IpPacket ipPacket)
    {
        // ReSharper disable once ForCanBeConvertedToForeach
        var session = GetSessionByVirtualIp(ipPacket.DestinationAddress);
        if (session == null)
            throw new Exception("SessionManager could not find the session for the packet.");

        session.Adapter_PacketReceived(this, ipPacket);
    }

    public Session? GetSessionByVirtualIp(IPAddress virtualIpAddress)
    {
        return _virtualIpManager.FindSession(virtualIpAddress);
    }


    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        // sync sessions
        try {
            await Sync(force: true).VhConfigureAwait();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not sync sessions in disposing.");
            // Dispose should not throw any error
        }

        Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _heartbeatJob.Dispose();
        _sessionLocalService.Dispose();

        // dispose all sessions
        VhLogger.Instance.LogDebug("Disposing all sessions...");
        foreach (var session in Sessions.Values)
            session.Dispose();
    }
}
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

public class SessionManager : IAsyncDisposable, IJob
{
    private readonly IAccessManager _accessManager;
    private readonly ISocketFactory _socketFactory;
    private readonly IVpnAdapter? _vpnAdapter;
    private byte[] _serverSecret;
    private readonly TimeSpan _deadSessionTimeout;
    private readonly JobSection _heartbeatSection;
    private readonly SessionLocalService _sessionLocalService;
    private readonly VirtualIpManager _virtualIpManager;

    public string ApiKey { get; private set; }
    public INetFilter NetFilter { get; }
    public JobSection JobSection { get; } = new(nameof(SessionManager));
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
        _heartbeatSection = new JobSection(options.HeartbeatInterval);
        _sessionLocalService = new SessionLocalService(Path.Combine(storagePath, "sessions"));
        _virtualIpManager = new VirtualIpManager(options.VirtualIpNetworkV4, options.VirtualIpNetworkV6,
            Path.Combine(storagePath, "last-virtual-ips.json"));

        Tracker = tracker;
        ApiKey = HttpUtil.GetApiKey(_serverSecret, TunnelDefaults.HttpPassCheck);
        NetFilter = netFilter;
        ServerVersion = serverVersion;
        if (_vpnAdapter != null)
            _vpnAdapter.PacketReceived += VpnAdapter_PacketReceived;

        JobRunner.Default.Add(this);
    }

    private async Task<Session> CreateSessionInternal(
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
        await session.DisposeAsync().VhConfigureAwait();
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
        var session = await CreateSessionInternal(sessionResponseEx, ipEndPointPair,
                helloRequest.RequestId, isRecovery: false)
            .VhConfigureAwait();

        // Anonymous Report to GA
        _ = GaTrackNewSession(helloRequest.ClientInfo);

        VhLogger.Instance.LogInformation(GeneralEventId.Session,
            "New session has been created. SessionId: {SessionId}",
            VhLogger.FormatSessionId(session.SessionId));

        return sessionResponseEx;
    }

    private Task GaTrackNewSession(ClientInfo clientInfo)
    {
        if (Tracker == null)
            return Task.CompletedTask;

        // track new session
        var serverVersion = ServerVersion.ToString(3);
        return Tracker.Track([
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
                    await session.DisposeAsync().VhConfigureAwait();
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
            session = await CreateSessionInternal(sessionResponse, ipEndPointPair, "recovery", isRecovery: true).VhConfigureAwait();
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

    public async Task RunJob()
    {
        // anonymous heart_beat reporter
        await _heartbeatSection.Enter(SendHeartbeat).VhConfigureAwait();
    }

    private Task SendHeartbeat()
    {
        if (Tracker == null)
            return Task.CompletedTask;

        return Tracker.Track(new TrackEvent {
            EventName = "heartbeat",
            Parameters = new Dictionary<string, object> {
                { "session_count", Sessions.Count(x => !x.Value.IsDisposed) }
            }
        });
    }

    private void DisposeExpiredSessions()
    {
        VhLogger.Instance.LogDebug("Disposing expired sessions...");
        var utcNow = DateTime.UtcNow;
        var timeoutSessions = Sessions.Values
            .Where(x => !x.IsDisposed && x.SessionResponseEx.AccessUsage?.ExpirationTime < utcNow);

        foreach (var session in timeoutSessions) {
            session.SessionResponseEx = new SessionResponse {
                ErrorCode = SessionErrorCode.AccessExpired
            };
            _ = session.DisposeAsync();
        }
    }

    // remove sessions from memory that are idle but not disposed yet
    private void RemoveIdleSessions()
    {
        VhLogger.Instance.LogDebug("Disposing all idle sessions...");
        var minSessionActivityTime = FastDateTime.Now - SessionOptions.TimeoutValue;
        var timeoutSessions = Sessions
            .Where(x =>
                !x.Value.IsDisposed &&
                !x.Value.IsSyncRequired &&
                x.Value.LastActivityTime < minSessionActivityTime)
            .ToArray(); // make sure make a copy to avoid modification in the loop

        foreach (var session in timeoutSessions) {
            if (session.Value.Traffic.Total > 0) {
                session.Value.SetSyncRequired(); // let's remove it in the next sync
            }
            else {
                RemoveSession(session.Value);
            }
        }
    }
    private void DisposeFailedSessions()
    {
        VhLogger.Instance.LogDebug("Process all failed sessions...");
        var failedSessions = Sessions
            .Where(x =>
                !x.Value.IsDisposed &&
                !x.Value.IsSyncRequired &&
                x.Value.SessionResponseEx.ErrorCode != SessionErrorCode.Ok);

        foreach (var failedSession in failedSessions) {
            _ = failedSession.Value.DisposeAsync().VhConfigureAwait();
        }
    }

    // remove sessions that are disposed a long time
    private void ProcessDeadSessions()
    {
        VhLogger.Instance.LogDebug("Disposing all disposed sessions...");
        var utcNow = DateTime.UtcNow;
        var deadSessions = Sessions.Values
            .Where(x =>
                x.IsDisposed &&
                !x.IsSyncRequired &&
                utcNow - x.DisposedTime > _deadSessionTimeout)
            .ToArray(); // make sure make a copy to avoid modification in the loop

        // remove dead sessions
        foreach (var session in deadSessions)
            RemoveSession(session);
    }

    public void RemoveSession(Session session)
    {
        _ = session.DisposeAsync();
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
        foreach (var pair in sessionResponses) {
            if (pair.Value.ErrorCode == SessionErrorCode.Ok) continue;
            if (Sessions.TryGetValue(pair.Key, out var session)) {
                session.SessionResponseEx = pair.Value;
            }
        }

        RemoveIdleSessions(); // dispose idle sessions
        DisposeExpiredSessions(); // dispose expired sessions
        DisposeFailedSessions(); // dispose failed sessions
        ProcessDeadSessions(); // remove dead sessions

        // clear usage if sent successfully
        _pendingUsages = [];
    }

    private readonly AsyncLock _syncLock = new();

    public async Task Sync(bool force = false)
    {
        using var lockResult = await _syncLock.LockAsync().VhConfigureAwait();
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

    private void VpnAdapter_PacketReceived(object sender, PacketReceivedEventArgs e)
    {
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var i = 0; i < e.IpPackets.Count; i++) {
            var ipPacket = e.IpPackets[i];
            var session = GetSessionByVirtualIp(ipPacket.DestinationAddress);
            if (session == null) {
                // log dropped packet
                if (VhLogger.IsDiagnoseMode)
                    PacketLogger.LogPacket(ipPacket, "Could not find session for packet destination.");
                return;
            }

            session.Proxy_OnPacketReceived(ipPacket);
        }
    }

    public Session? GetSessionByVirtualIp(IPAddress virtualIpAddress)
    {
        return _virtualIpManager.FindSession(virtualIpAddress);
    }

    private bool _disposed;
    private readonly AsyncLock _disposeLock = new();

    public async ValueTask DisposeAsync()
    {
        using var lockResult = await _disposeLock.LockAsync().VhConfigureAwait();
        if (_disposed) return;
        _disposed = true;

        // sync sessions
        try {
            await Sync(force: true);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not sync sessions in disposing.");
            // Dispose should not throw any error
        }

        // dispose all sessions
        await Task.WhenAll(Sessions.Values.Select(x => x.DisposeAsync().AsTask())).VhConfigureAwait();
    }
}
﻿using System.Collections.Concurrent;
using System.Text.Json;
using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Jobs;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
using VpnHood.Common.Trackers;
using VpnHood.Common.Utils;
using VpnHood.Server.Abstractions;
using VpnHood.Server.Access.Configurations;
using VpnHood.Server.Access.Managers;
using VpnHood.Server.Access.Messaging;
using VpnHood.Server.Exceptions;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Factory;
using VpnHood.Tunneling.Messaging;
using VpnHood.Tunneling.Utils;

namespace VpnHood.Server;

public class SessionManager : IAsyncDisposable, IJob
{
    private readonly IAccessManager _accessManager;
    private readonly SocketFactory _socketFactory;
    private byte[] _serverSecret;
    private readonly TimeSpan _deadSessionTimeout;
    private readonly JobSection _heartbeatSection;

    public string ApiKey { get; private set; }
    public INetFilter NetFilter { get; }
    public JobSection JobSection { get; } = new(nameof(SessionManager));
    public Version ServerVersion { get; }
    public ConcurrentDictionary<ulong, Session> Sessions { get; } = new();
    public TrackingOptions TrackingOptions { get; set; } = new();
    public SessionOptions SessionOptions { get; set; } = new();
    public ITracker? Tracker { get; }

    public byte[] ServerSecret {
        get => _serverSecret;
        set {
            ApiKey = HttpUtil.GetApiKey(value, TunnelDefaults.HttpPassCheck);
            _serverSecret = value;
        }
    }

    internal SessionManager(IAccessManager accessManager,
        INetFilter netFilter,
        SocketFactory socketFactory,
        ITracker? tracker,
        Version serverVersion,
        SessionManagerOptions options)
    {
        _accessManager = accessManager ?? throw new ArgumentNullException(nameof(accessManager));
        _socketFactory = socketFactory ?? throw new ArgumentNullException(nameof(socketFactory));
        _serverSecret = VhUtil.GenerateKey(128);
        _deadSessionTimeout = options.DeadSessionTimeout;
        _heartbeatSection = new JobSection(options.HeartbeatInterval);
        Tracker = tracker;
        ApiKey = HttpUtil.GetApiKey(_serverSecret, TunnelDefaults.HttpPassCheck);
        NetFilter = netFilter;
        ServerVersion = serverVersion;
        JobRunner.Default.Add(this);
    }

    private async Task<Session> CreateSessionInternal(
        SessionResponseEx sessionResponseEx,
        IPEndPointPair ipEndPointPair,
        string requestId)
    {
        var extraData = sessionResponseEx.ExtraData != null
            ? VhUtil.JsonDeserialize<SessionExtraData>(sessionResponseEx.ExtraData)
            : new SessionExtraData();

        var session = new Session(_accessManager, sessionResponseEx, NetFilter, _socketFactory,
            SessionOptions, TrackingOptions, extraData, protocolVersion: sessionResponseEx.ProtocolVersion);

        // add to sessions
        if (Sessions.TryAdd(session.SessionId, session))
            return session;

        session.SessionResponse.ErrorMessage = "Could not add session to collection.";
        session.SessionResponse.ErrorCode = SessionErrorCode.SessionError;
        await session.DisposeAsync().VhConfigureAwait();
        throw new ServerSessionException(ipEndPointPair.RemoteEndPoint, session,
            session.SessionResponse, requestId);
    }

    public async Task<SessionResponseEx> CreateSession(HelloRequest helloRequest, IPEndPointPair ipEndPointPair)
    {
        // validate the token
        VhLogger.Instance.Log(LogLevel.Trace, "Validating the request by the access server. TokenId: {TokenId}",
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
            IsIpV6Supported = helloRequest.IsIpV6Supported
        }).VhConfigureAwait();

        // Access Error should not pass to the client in create session
        if (sessionResponseEx.ErrorCode is SessionErrorCode.AccessError)
            throw new ServerUnauthorizedAccessException(sessionResponseEx.ErrorMessage ?? "Access Error.",
                ipEndPointPair, helloRequest);

        if (sessionResponseEx.ErrorCode != SessionErrorCode.Ok)
            throw new ServerSessionException(ipEndPointPair.RemoteEndPoint, sessionResponseEx, helloRequest);

        // create the session and add it to list
        var session = await CreateSessionInternal(sessionResponseEx, ipEndPointPair, helloRequest.RequestId)
            .VhConfigureAwait();

        // Anonymous Report to GA
        _ = GaTrackNewSession(helloRequest.ClientInfo);

        VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Session,
            $"New session has been created. SessionId: {VhLogger.FormatSessionId(session.SessionId)}");
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

    private async Task<Session> RecoverSession(RequestBase sessionRequest, IPEndPointPair ipEndPointPair)
    {
        using var recoverLock = await AsyncLock.LockAsync($"Recover_session_{sessionRequest.SessionId}").VhConfigureAwait();
        var session = GetSessionById(sessionRequest.SessionId);
        if (session != null)
            return session;

        // Get session from the access server
        VhLogger.Instance.LogTrace(GeneralEventId.Session,
            "Trying to recover a session from the access server. SessionId: {SessionId}",
            VhLogger.FormatSessionId(sessionRequest.SessionId));

        try {
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

            // create the session even if it contains error to prevent many calls
            session = await CreateSessionInternal(sessionResponse, ipEndPointPair, "recovery").VhConfigureAwait();
            VhLogger.Instance.LogInformation(GeneralEventId.Session,
                "Session has been recovered. SessionId: {SessionId}",
                VhLogger.FormatSessionId(sessionRequest.SessionId));

            return session;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogInformation(GeneralEventId.Session,
                "Could not recover a session. SessionId: {SessionId}",
                VhLogger.FormatSessionId(sessionRequest.SessionId));

            var sessionResponseEx = ex is ServerSessionException { SessionResponse: SessionResponseEx response }
                ? response
                : new SessionResponseEx {
                    ErrorCode = SessionErrorCode.SessionError,
                    SessionId = sessionRequest.SessionId,
                    SessionKey = sessionRequest.SessionKey,
                    CreatedTime = DateTime.UtcNow,
                    ErrorMessage = ex.Message
                };

            // Create a dead session to cache multiple requests
            await CreateSessionInternal(sessionResponseEx, ipEndPointPair, "dead-recovery").VhConfigureAwait();
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

        if (session.SessionResponse.ErrorCode != SessionErrorCode.Ok)
            throw new ServerSessionException(ipEndPointPair.RemoteEndPoint, session, session.SessionResponse,
                requestBase.RequestId);

        // unexpected close (disposed without error code)
        if (session.IsDisposed)
            throw new ServerSessionException(ipEndPointPair.RemoteEndPoint, session,
                new SessionResponse {
                    ErrorCode = SessionErrorCode.SessionClosed,
                    ErrorMessage = session.SessionResponse.ErrorMessage,
                    AccessUsage = session.SessionResponse.AccessUsage,
                    SuppressedBy = session.SessionResponse.SuppressedBy,
                    RedirectHostEndPoint = session.SessionResponse.RedirectHostEndPoint
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
        VhLogger.Instance.LogTrace("Dispose expired sessions.");
        var utcNow = DateTime.UtcNow;
        var timeoutSessions = Sessions.Values
            .Where(x => !x.IsDisposed && x.SessionResponse.AccessUsage?.ExpirationTime < utcNow);

        foreach (var session in timeoutSessions) {
            session.SessionResponse = new SessionResponse {
                ErrorCode = SessionErrorCode.AccessExpired
            };
            _ = session.DisposeAsync();
        }
    }

    private void DisposeFailedSessions()
    {
        VhLogger.Instance.LogTrace("Process failed sessions.");
        var failedSessions = Sessions
            .Where(x =>
                !x.Value.IsDisposed &&
                !x.Value.IsSyncRequired &&
                x.Value.SessionResponse.ErrorCode != SessionErrorCode.Ok);

        foreach (var failedSession in failedSessions) {
            _ = failedSession.Value.DisposeAsync().VhConfigureAwait();
        }
    }

    private void DisposeAndRemoveIdleSessions()
    {
        VhLogger.Instance.LogTrace("Dispose idle sessions.");
        var minSessionActivityTime = FastDateTime.Now - SessionOptions.TimeoutValue;
        var timeoutSessions = Sessions
            .Where(x =>
                !x.Value.IsDisposed &&
                !x.Value.IsSyncRequired &&
                x.Value.LastActivityTime < minSessionActivityTime)
            .ToArray();

        foreach (var session in timeoutSessions) {
            if (session.Value.Traffic.Total > 0) {
                session.Value.SetSyncRequired(); // let's remove it in the next sync
            }
            else {
                _ = session.Value.DisposeAsync().VhConfigureAwait();
                Sessions.TryRemove(session.Key, out _); // we should not have disposed session without error code
            }
        }
    }

    private void ProcessDeadSessions()
    {
        VhLogger.Instance.LogTrace("Dispose disposed sessions.");
        var utcNow = DateTime.UtcNow;
        var deadSessions = Sessions.Values
            .Where(x =>
                x.IsDisposed &&
                !x.IsSyncRequired &&
                utcNow - x.DisposedTime > _deadSessionTimeout)
            .ToArray();

        foreach (var session in deadSessions)
            Sessions.TryRemove(session.SessionId, out _);
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
                    Closed = x.SessionResponse.ErrorCode == SessionErrorCode.SessionClosed
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
                session.SessionResponse = pair.Value;
            }
        }

        DisposeAndRemoveIdleSessions(); // dispose idle sessions
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
        session.SessionResponse.ErrorCode = SessionErrorCode.SessionClosed;
        session.SetSyncRequired();
        await Sync();
    }


    private bool _disposed;
    private readonly AsyncLock _disposeLock = new();
    public async ValueTask DisposeAsync()
    {
        using var lockResult = await _disposeLock.LockAsync().VhConfigureAwait();
        if (_disposed) return;
        _disposed = true;

        await Sync(force: true);
        await Task.WhenAll(Sessions.Values.Select(x => x.DisposeAsync().AsTask())).VhConfigureAwait();
    }
}

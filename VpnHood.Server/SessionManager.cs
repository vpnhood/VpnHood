using System.Collections.Concurrent;
using System.Text.Json;
using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Jobs;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
using VpnHood.Common.Trackers;
using VpnHood.Common.Utils;
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
    private readonly JobSection _heartbeatSection = new(TimeSpan.FromMinutes(10));

    public string ApiKey { get; private set; }
    public INetFilter NetFilter { get; }
    public JobSection JobSection { get; }
    public Version ServerVersion { get; }
    public ConcurrentDictionary<ulong, Session> Sessions { get; } = new();
    public TrackingOptions TrackingOptions { get; set; } = new();
    public SessionOptions SessionOptions { get; set; } = new();
    public ITracker? Tracker { get; }

    public byte[] ServerSecret
    {
        get => _serverSecret;
        set
        {
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
        Tracker = tracker;
        ApiKey = HttpUtil.GetApiKey(_serverSecret, TunnelDefaults.HttpPassCheck);
        NetFilter = netFilter;
        ServerVersion = serverVersion;
        JobSection = new JobSection(options.CleanupInterval, nameof(SessionManager));
        JobRunner.Default.Add(this);
    }

    public async Task SyncSessions()
    {
        // launch all syncs
        var syncTasks = Sessions.Values.Select(x => (x.SessionId, Task: JobRunner.RunNow(x)));

        // wait for all
        foreach (var syncTask in syncTasks)
        {
            try
            {
                await syncTask.Task.VhConfigureAwait();
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogError(GeneralEventId.Session, ex,
                    "Error in syncing a session. SessionId: {SessionId}", syncTask.SessionId);
            }
        }
    }

    private async Task<Session> CreateSessionInternal(
        SessionResponseEx sessionResponseEx,
        IPEndPointPair ipEndPointPair,
        string requestId)
    {
        var extraData = sessionResponseEx.ExtraData != null
            ? VhUtil.JsonDeserialize<SessionExtraData>(sessionResponseEx.ExtraData)
            : new SessionExtraData { ProtocolVersion = 3 };

        var session = new Session(_accessManager, sessionResponseEx, NetFilter, _socketFactory,
            SessionOptions, TrackingOptions, extraData);

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
        var extraData = JsonSerializer.Serialize(new SessionExtraData
        { ProtocolVersion = helloRequest.ClientInfo.ProtocolVersion });
        var sessionResponseEx = await _accessManager.Session_Create(new SessionRequestEx
        {
            HostEndPoint = ipEndPointPair.LocalEndPoint,
            ClientIp = ipEndPointPair.RemoteEndPoint.Address,
            ExtraData = extraData,
            ClientInfo = helloRequest.ClientInfo,
            EncryptedClientId = helloRequest.EncryptedClientId,
            TokenId = helloRequest.TokenId,
            ServerLocation = helloRequest.ServerLocation,
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
        var session = await CreateSessionInternal(sessionResponseEx, ipEndPointPair, helloRequest.RequestId).VhConfigureAwait();

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
        return Tracker.Track([new TrackEvent
        {
            EventName = TrackEventNames.PageView,
            Parameters = new Dictionary<string, object>
            {
                { "client_version", clientInfo.ClientVersion },
                { "server_version", serverVersion },
                { TrackParameterNames.PageTitle, $"server_version/{serverVersion}" },
                { TrackParameterNames.PageLocation, $"server_version/{serverVersion}" }
            }
        }]);
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

        try
        {
            var sessionResponse = await _accessManager.Session_Get(sessionRequest.SessionId,
                ipEndPointPair.LocalEndPoint, ipEndPointPair.RemoteEndPoint.Address)
                .VhConfigureAwait();

            // Check session key for recovery
            if (!sessionRequest.SessionKey.SequenceEqual(sessionResponse.SessionKey))
                throw new ServerUnauthorizedAccessException("Invalid SessionKey.", ipEndPointPair,
                    sessionRequest.SessionId);

            // session is authorized, so we can pass any error to client
            if (sessionResponse.ErrorCode != SessionErrorCode.Ok)
                throw new ServerSessionException(ipEndPointPair.RemoteEndPoint, sessionResponse, sessionRequest);

            // create the session even if it contains error to prevent many calls
            session = await CreateSessionInternal(sessionResponse, ipEndPointPair, "recovery").VhConfigureAwait();
            VhLogger.Instance.LogInformation(GeneralEventId.Session,
                "Session has been recovered. SessionId: {SessionId}",
                VhLogger.FormatSessionId(sessionRequest.SessionId));

            return session;
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogInformation(GeneralEventId.Session,
                "Could not recover a session. SessionId: {SessionId}",
                VhLogger.FormatSessionId(sessionRequest.SessionId));

            // Create a dead session if it is not created
            session = await CreateSessionInternal(new SessionResponseEx
            {
                ErrorCode = SessionErrorCode.SessionError,
                SessionId = sessionRequest.SessionId,
                SessionKey = sessionRequest.SessionKey,
                CreatedTime = DateTime.UtcNow,
                ErrorMessage = ex.Message
            }, ipEndPointPair, "dead-recovery").VhConfigureAwait();
            await session.DisposeAsync().VhConfigureAwait();
            throw;
        }
    }

    internal async Task<Session> GetSession(RequestBase requestBase, IPEndPointPair ipEndPointPair)
    {
        //get session
        var session = GetSessionById(requestBase.SessionId);
        if (session != null)
        {
            if (!requestBase.SessionKey.SequenceEqual(session.SessionKey))
                throw new ServerUnauthorizedAccessException("Invalid session key.", ipEndPointPair, session);
        }
        // try to restore session if not found
        else
        {
            session = await RecoverSession(requestBase, ipEndPointPair).VhConfigureAwait();
        }

        if (session.SessionResponse.ErrorCode != SessionErrorCode.Ok)
            throw new ServerSessionException(ipEndPointPair.RemoteEndPoint, session, session.SessionResponse,
                requestBase.RequestId);

        // unexpected close
        if (session.IsDisposed)
            throw new ServerSessionException(ipEndPointPair.RemoteEndPoint, session,
                new SessionResponse
                {
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

        // clean disposed sessions
        await Cleanup().VhConfigureAwait();
    }

    private Task SendHeartbeat()
    {
        if (Tracker == null)
            return Task.CompletedTask;

        return Tracker.Track(new TrackEvent
        {
            EventName = "heartbeat",
            Parameters = new Dictionary<string, object>
            {
                { "session_count", Sessions.Count(x => !x.Value.IsDisposed) }
            }
        });
    }

    private async Task CloseExpiredSessions()
    {
        var utcNow = DateTime.UtcNow;
        var timeoutSessions = Sessions.Values
            .Where(x => !x.IsDisposed && x.SessionResponse.AccessUsage?.ExpirationTime < utcNow);

        foreach (var session in timeoutSessions)
            await session.Sync().VhConfigureAwait();
    }

    private async Task RemoveTimeoutSession()
    {
        VhLogger.Instance.LogTrace("Remove timeout sessions.");
        var minSessionActivityTime = FastDateTime.Now - SessionOptions.TimeoutValue;
        var timeoutSessions = Sessions
            .Where(x => x.Value.IsDisposed || x.Value.LastActivityTime < minSessionActivityTime)
            .ToArray();

        foreach (var session in timeoutSessions)
        {
            Sessions.Remove(session.Key, out _);
            await session.Value.DisposeAsync().VhConfigureAwait();
        }
    }


    private async Task Cleanup()
    {
        await CloseExpiredSessions().VhConfigureAwait();
        await RemoveTimeoutSession().VhConfigureAwait();
    }

    public Session? GetSessionById(ulong sessionId)
    {
        Sessions.TryGetValue(sessionId, out var session);
        return session;
    }

    /// <summary>
    ///     Close session in this server and AccessManager
    /// </summary>
    /// <param name="sessionId"></param>
    public async Task CloseSession(ulong sessionId)
    {
        // find in session
        if (Sessions.TryGetValue(sessionId, out var session))
            await session.Close().VhConfigureAwait();
    }

    private bool _disposed;
    private readonly AsyncLock _disposeLock = new();
    public async ValueTask DisposeAsync()
    {
        using var lockResult = await _disposeLock.LockAsync().VhConfigureAwait();
        if (_disposed) return;
        _disposed = true;

        await Task.WhenAll(Sessions.Values.Select(x => x.DisposeAsync().AsTask())).VhConfigureAwait();
    }
}


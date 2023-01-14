using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VpnHood.Common.JobController;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
using VpnHood.Common.Trackers;
using VpnHood.Common.Utils;
using VpnHood.Server.Exceptions;
using VpnHood.Server.Messaging;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Factory;
using VpnHood.Tunneling.Messaging;

namespace VpnHood.Server;

public class SessionManager : IDisposable, IAsyncDisposable, IJob
{
    private readonly IAccessServer _accessServer;
    private readonly SocketFactory _socketFactory;
    private readonly ITracker? _tracker;

    public JobSection JobSection { get; } = new(TimeSpan.FromMinutes(10));
    public string ServerVersion { get; }
    public ConcurrentDictionary<uint, Session> Sessions { get; } = new();
    public TrackingOptions TrackingOptions { get; set; } = new();
    public SessionOptions SessionOptions { get; set; } = new();
    public SessionManager(IAccessServer accessServer, SocketFactory socketFactory, ITracker? tracker)
    {
        _accessServer = accessServer ?? throw new ArgumentNullException(nameof(accessServer));
        _socketFactory = socketFactory ?? throw new ArgumentNullException(nameof(socketFactory));
        _tracker = tracker;
        ServerVersion = typeof(SessionManager).Assembly.GetName().Version.ToString();
        JobRunner.Default.Add(this);
    }

    public Task SyncSessions()
    {
        var tasks = Sessions.Values.Select(x => x.Sync());
        return Task.WhenAll(tasks);
    }

    public void Dispose()
    {
        DisposeAsync().GetAwaiter().GetResult();
    }

    private bool _disposed;
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await Task.WhenAll(Sessions.Values.Select(x => x.DisposeAsync().AsTask()));
        await SyncSessions();
    }

    private async Task<Session> CreateSessionInternal(SessionResponse sessionResponse,
        IPEndPoint localEndPoint, HelloRequest? helloRequest)
    {
        var session = new Session(_accessServer, sessionResponse, _socketFactory,
            localEndPoint, SessionOptions, TrackingOptions, helloRequest);

        // add to sessions
        if (Sessions.TryAdd(session.SessionId, session))
            return session;

        await session.DisposeAsync(true);
        throw new Exception($"Could not add session to collection: SessionId: {VhLogger.FormatSessionId(session.SessionId)}");

    }

    public async Task<SessionResponse> CreateSession(HelloRequest helloRequest, IPEndPointPair ipEndPointPair)
    {
        // validate the token
        VhLogger.Instance.Log(LogLevel.Trace, $"Validating the request by the access server. TokenId: {VhLogger.FormatId(helloRequest.TokenId)}");
        var sessionResponse = await _accessServer.Session_Create(new SessionRequestEx(helloRequest, ipEndPointPair.LocalEndPoint)
        {
            ClientIp = ipEndPointPair.RemoteEndPoint.Address,
        });

        // Access Error should not pass to the client in create session
        if (sessionResponse.ErrorCode is SessionErrorCode.AccessError)
            throw new ServerUnauthorizedAccessException(sessionResponse.ErrorMessage ?? "Access Error.", ipEndPointPair, helloRequest);

        if (sessionResponse.ErrorCode != SessionErrorCode.Ok)
            throw new ServerSessionException(ipEndPointPair.RemoteEndPoint, sessionResponse, helloRequest);

        // create the session and add it to list
        var session = await CreateSessionInternal(sessionResponse, ipEndPointPair.RemoteEndPoint, helloRequest);

        _ = _tracker?.TrackEvent("Usage", "SessionCreated");
        VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Session, $"New session has been created. SessionId: {VhLogger.FormatSessionId(session.SessionId)}");
        return sessionResponse;
    }

    private async Task<Session> RecoverSession(RequestBase sessionRequest, IPEndPointPair ipEndPointPair)
    {
        using var recoverLock = await AsyncLock.LockAsync($"Recover_session_{sessionRequest.SessionId}");
        var session = GetSessionById(sessionRequest.SessionId);
        if (session != null)
            return session;

        // Get session from the access server
        VhLogger.Instance.LogTrace(GeneralEventId.Session,
            "Trying to recover a session from the access server. SessionId: {SessionId}",
            VhLogger.FormatSessionId(sessionRequest.SessionId));

        try
        {
            var sessionResponse = await _accessServer.Session_Get(sessionRequest.SessionId,
                ipEndPointPair.LocalEndPoint, ipEndPointPair.RemoteEndPoint.Address);

            // Check session key for recovery
            if (!sessionRequest.SessionKey.SequenceEqual(sessionResponse.SessionKey))
                throw new ServerUnauthorizedAccessException("Invalid SessionKey.", ipEndPointPair, sessionRequest.SessionId);

            // session is authorized so we can pass any error to client
            if (sessionResponse.ErrorCode != SessionErrorCode.Ok)
                throw new ServerSessionException(ipEndPointPair.RemoteEndPoint, sessionResponse, sessionRequest);

            // create the session even if it contains error to prevent many calls
            session = await CreateSessionInternal(sessionResponse, ipEndPointPair.LocalEndPoint, null);
            VhLogger.Instance.LogInformation(GeneralEventId.Session, "Session has been recovered. SessionId: {SessionId}",
                VhLogger.FormatSessionId(sessionRequest.SessionId));

            return session;
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogInformation(GeneralEventId.Session, "Could not recover a session. SessionId: {SessionId}",
                VhLogger.FormatSessionId(sessionRequest.SessionId));

            // Create a dead session if it is not created
            session = await CreateSessionInternal(new SessionResponse(SessionErrorCode.SessionError)
            {
                SessionId = sessionRequest.SessionId,
                SessionKey = sessionRequest.SessionKey,
                CreatedTime = DateTime.UtcNow,
                ErrorMessage = ex.Message

            }, ipEndPointPair.LocalEndPoint, null);

            await session.DisposeAsync(false, false);
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
            session = await RecoverSession(requestBase, ipEndPointPair);
        }

        if (session.SessionResponseBase.ErrorCode != SessionErrorCode.Ok)
            throw new ServerSessionException(ipEndPointPair.RemoteEndPoint, session, session.SessionResponseBase);

        return session;
    }

    public Task RunJob()
    {
        return Cleanup();
    }

    private readonly AsyncLock _cleanupLock = new();
    private async Task Cleanup()
    {
        using var cleaningLock = await _cleanupLock.LockAsync(TimeSpan.Zero);
        if (!cleaningLock.Succeeded)
            return;

        // update all sessions status
        var minSessionActivityTime = FastDateTime.Now - SessionOptions.Timeout;
        var timeoutSessions = Sessions
            .Where(x => x.Value.IsDisposed || x.Value.LastActivityTime < minSessionActivityTime)
            .ToArray();

        foreach (var session in timeoutSessions)
        {
            await session.Value.DisposeAsync();
            Sessions.Remove(session.Key, out _);
        }
    }

    public Session? GetSessionById(uint sessionId)
    {
        Sessions.TryGetValue(sessionId, out var session);
        return session;
    }

    /// <summary>
    ///     Close session in this server and AccessServer
    /// </summary>
    /// <param name="sessionId"></param>
    public async Task CloseSession(uint sessionId)
    {
        // find in session
        if (Sessions.TryGetValue(sessionId, out var session))
        {
            if (session.SessionResponseBase.ErrorCode == SessionErrorCode.Ok)
                session.SessionResponseBase.ErrorCode = SessionErrorCode.SessionClosed;
            await session.DisposeAsync(true);
        }
    }
}
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Collections;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Trackers;
using VpnHood.Server.Messaging;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Factory;
using VpnHood.Tunneling.Messaging;

namespace VpnHood.Server;

public class SessionManager : IDisposable, IAsyncDisposable
{
    private readonly IAccessServer _accessServer;
    private readonly Timer _cleanUpTimer;
    private readonly SocketFactory _socketFactory;
    private readonly ITracker? _tracker;
    private readonly TimeoutDictionary<long, TimeoutItem<SemaphoreSlim>> _recoverSemaphores = new(TimeSpan.FromMinutes(10));

    public string ServerVersion { get; }
    public ConcurrentDictionary<uint, Session> Sessions { get; } = new();
    public TrackingOptions TrackingOptions { get; set; } = new();
    public SessionOptions SessionOptions { get; set; } = new();
    public SessionManager(IAccessServer accessServer, SocketFactory socketFactory, ITracker? tracker)
    {
        _accessServer = accessServer ?? throw new ArgumentNullException(nameof(accessServer));
        _socketFactory = socketFactory ?? throw new ArgumentNullException(nameof(socketFactory));
        _tracker = tracker;
        _cleanUpTimer = new Timer(_ => Cleanup(), null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
        ServerVersion = typeof(SessionManager).Assembly.GetName().Version.ToString();
    }

    public Task SyncSessions()
    {
        var tasks = Sessions.Values.Select(x => x.Sync());
        return Task.WhenAll(tasks);
    }

    public void Dispose()
    {
        _cleanUpTimer.Dispose();
        foreach (var session in Sessions.Values)
            session.Dispose();

        _recoverSemaphores.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await _cleanUpTimer.DisposeAsync();
        await SyncSessions();
    }

    private Session CreateSessionInternal(SessionResponse sessionResponse, IPEndPoint hostEndPoint)
    {
        var session = new Session(_accessServer, sessionResponse, _socketFactory,
            hostEndPoint, SessionOptions, TrackingOptions);
        
        // create a dispose session to cache the sessionResponse from access server 
        if (sessionResponse.ErrorCode != SessionErrorCode.Ok)
            session.Dispose(false, false);

        // add to sessions
        if (!Sessions.TryAdd(session.SessionId, session))
        {
            session.Dispose(true);
            throw new Exception($"Could not add session to collection: SessionId: {VhLogger.FormatSessionId(session.SessionId)}");
        }

        return session;
    }

    public async Task<SessionResponse> CreateSession(HelloRequest helloRequest, IPEndPoint hostEndPoint, IPAddress clientIp)
    {
        // validate the token
        VhLogger.Instance.Log(LogLevel.Trace, $"Validating the request by the access server. TokenId: {VhLogger.FormatId(helloRequest.TokenId)}");
        var sessionResponse = await _accessServer.Session_Create(new SessionRequestEx(helloRequest, hostEndPoint)
        {
            ClientIp = clientIp
        });
        
        // Access Error should not pass to the client in create session
        if (sessionResponse.ErrorCode is SessionErrorCode.AccessError )
            throw new UnauthorizedAccessException(sessionResponse.ErrorMessage);

        if (sessionResponse.ErrorCode != SessionErrorCode.Ok)
            throw new SessionException(sessionResponse);

        // create the session and add it to list
        var session = CreateSessionInternal(sessionResponse, hostEndPoint);

        _ = _tracker?.TrackEvent("Usage", "SessionCreated");
        VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Session, $"New session has been created. SessionId: {VhLogger.FormatSessionId(session.SessionId)}");
        return sessionResponse;
    }

    private async Task<Session> RecoverSession(RequestBase sessionRequest, IPEndPoint hostEndPoint, IPAddress? clientIp)
    {
        // create a semaphore for each session so all requests of each session will wait
        var isNew = false;
        var semaphore = _recoverSemaphores.GetOrAdd(sessionRequest.SessionId, _ =>
        {
            isNew = true;
            // semaphores should not be disposed when other threads wait for them and will cause unpredictable result
            return new TimeoutItem<SemaphoreSlim>(new SemaphoreSlim(0), false);
        }).Value;

        try
        {
            // try to recover for access server only once and queue other request behind semaphore
            if (!isNew)
            {
                await semaphore.WaitAsync(TimeSpan.FromMinutes(2));
                return GetSessionById(sessionRequest.SessionId) 
                       ?? throw new KeyNotFoundException($"Invalid SessionId: SessionId: {VhLogger.FormatSessionId(sessionRequest.SessionId)}");
            }

            VhLogger.Instance.LogInformation(GeneralEventId.Session, "Trying to recover a session from the access server...");
            var sessionResponse = await _accessServer.Session_Get(sessionRequest.SessionId, hostEndPoint, clientIp);
            if (!sessionRequest.SessionKey.SequenceEqual(sessionResponse.SessionKey))
                throw new UnauthorizedAccessException("Invalid SessionKey.");

            var session = CreateSessionInternal(sessionResponse, hostEndPoint);
            VhLogger.Instance.LogTrace(GeneralEventId.Session, "SessionOptions has been recovered.");

            // session is authorized so we can pass any error to client
            if (sessionResponse.ErrorCode != SessionErrorCode.Ok)
                throw new SessionException(sessionResponse);
            return session;
        }
        finally
        {
            semaphore.Release();
        }
    }

    internal async Task<Session> GetSession(RequestBase sessionRequest, IPEndPoint hostEndPoint, IPAddress? clientIp)
    {
        //get session
        var session = GetSessionById(sessionRequest.SessionId);
        if (session != null)
        {
            if (!sessionRequest.SessionKey.SequenceEqual(session.SessionKey))
                throw new UnauthorizedAccessException("Invalid SessionKey.");
        }
        // try to restore session if not found
        else
        {
            session = await RecoverSession(sessionRequest, hostEndPoint, clientIp);
        }

        if (session.SessionResponse.ErrorCode != SessionErrorCode.Ok)
            throw new SessionException(session.SessionResponse);

        return session;
    }

    private void Cleanup()
    {
        // update all sessions status
        var minSessionActivityTime = DateTime.Now - SessionOptions.Timeout;
        var timeoutSessions = Sessions.Where(x => x.Value.IsDisposed || x.Value.LastActivityTime < minSessionActivityTime).ToArray();
        foreach (var session in timeoutSessions)
        {
            session.Value.Dispose();
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
    public void CloseSession(uint sessionId)
    {
        // find in session
        if (Sessions.TryGetValue(sessionId, out var session))
        {
            if (session.SessionResponse.ErrorCode==SessionErrorCode.Ok)
                session.SessionResponse.ErrorCode = SessionErrorCode.SessionClosed;
            session.Dispose(true);
        }
    }
}
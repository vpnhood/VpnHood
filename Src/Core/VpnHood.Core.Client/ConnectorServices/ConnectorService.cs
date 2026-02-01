using Microsoft.Extensions.Logging;
using System.Net;
using VpnHood.Core.Client.Exceptions;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Connections;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Client.ConnectorServices;

internal class ConnectorService(
    ConnectorServiceOptions options)
    : ConnectorServiceBase(options)
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private int _isDisposed;


    public void Init(int protocolVersion, byte[]? serverSecret, TimeSpan requestTimeout, TimeSpan tcpReuseTimeout,
        bool useWebSocket)
    {
        RequestTimeout = requestTimeout;
        base.Init(protocolVersion, serverSecret, tcpReuseTimeout, useWebSocket && protocolVersion >= 9);
    }

    public async Task<ConnectorRequestResult<T>> SendRequest<T>(ClientRequest request,
        CancellationToken cancellationToken)
        where T : SessionResponse
    {
        using var timeoutCts = new CancellationTokenSource(RequestTimeout);
        using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutCts.Token, cancellationToken, _cancellationTokenSource.Token);

        // callback to reset timeout on each proxy attempt
        // ReSharper disable once AccessToDisposedClosure
        void OnAttempt() => timeoutCts.CancelAfter(RequestTimeout);

        try {
            var eventId = GetRequestEventId(request);
            request.RequestId += ":client";
            VhLogger.Instance.LogDebug(eventId,
                "Sending a request. RequestCode: {RequestCode}, RequestId: {RequestId}",
                (RequestCode)request.RequestCode, request.RequestId);

            await using var mem = new MemoryStream();
            mem.WriteByte(1);
            mem.WriteByte(request.RequestCode);
            await StreamUtils.WriteObjectAsync(mem, request, requestCts.Token).Vhc();

            // ReSharper disable once AccessToDisposedClosure
            var ret = await SendRequest<T>(mem.ToArray(), request.RequestId, OnAttempt, requestCts.Token).Vhc();

            // log the response
            VhLogger.Instance.LogDebug(eventId, "Received a response... ErrorCode: {ErrorCode}.",
                ret.Response.ErrorCode);

            lock (Status) Status.RequestCount++;
            return ret;
        }
        catch (Exception) when (timeoutCts.IsCancellationRequested) {
            throw new TimeoutException(
                $"Could not send the {(RequestCode)request.RequestCode} request in the given time.");
        }
    }

    private async Task<ConnectorRequestResult<T>> SendRequest<T>(ReadOnlyMemory<byte> request, string requestId,
        Action onAttempt, CancellationToken cancellationToken)
        where T : SessionResponse
    {
        // try reuse
        var reusableConnection = GetFreeConnection();
        if (reusableConnection != null) {
            try {
                VhLogger.Instance.LogDebug(GeneralEventId.Stream,
                    "A shared Connection has been reused. ConnectionId: {ConnectionId}, LocalEp: {LocalEp}",
                    reusableConnection.Id, reusableConnection.LocalEndPoint);

                // send the request
                await reusableConnection.Stream.WriteAsync(request, cancellationToken).Vhc();
                var response = await ReadSessionResponse<T>(reusableConnection.Stream, cancellationToken).Vhc();
                lock (Status) Status.ReusedConnectionSucceededCount++;
                return new ConnectorRequestResult<T> {
                    Response = response,
                    Connection = reusableConnection
                };
            }
            catch (SessionException) {
                // let the caller handle the exception. there is no error in connection
                await reusableConnection.DisposeAsync();
                throw;
            }
            catch (Exception ex) {
                // dispose the connection and retry with new connection
                lock (Status) Status.ReusedConnectionFailedCount++;
                reusableConnection.PreventReuse();
                await reusableConnection.DisposeAsync();
                VhLogger.Instance.LogError(GeneralEventId.Stream, ex,
                    "Error in reusing the Connection. Try a new connection. ConnectionId: {ConnectionId}, RequestId: {requestId}",
                    reusableConnection.Id, requestId);
            }
        }

        // create a new connection
        var connection = await GetTlsConnectionToServer(requestId + ":tunnel", request.Length,
            onAttempt, cancellationToken).Vhc();

        // send request
        try {
            // send the request
            await connection.Stream.WriteAsync(request, cancellationToken).Vhc();
            await connection.Stream.FlushAsync(cancellationToken);

            // parse the HTTP request
            if (connection.RequireHttpResponse) {
                connection.RequireHttpResponse = false;
                var responseMessage = await HttpUtils.ReadResponse(connection.Stream, cancellationToken).Vhc();
                if (responseMessage.StatusCode == HttpStatusCode.Unauthorized)
                    throw new UnauthorizedAccessException();

                // other error response will be handled by the SessionResponse
            }

            // read the response
            var response2 = await ReadSessionResponse<T>(connection.Stream, cancellationToken).Vhc();
            return new ConnectorRequestResult<T> {
                Response = response2,
                Connection = connection
            };
        }
        catch (SessionException) {
            // let the caller handle the exception. there is no error in connection
            connection.Dispose();
            throw;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(GeneralEventId.Stream, ex,
                "Error in sending a request. ConnectionId: {ConnectionId}, RequestId: {requestId}",
                connection.Id, requestId);

            // dispose the connection
            (connection as ReusableConnection)?.PreventReuse();
            connection.Dispose();
            throw;
        }
    }

    private static async Task<T> ReadSessionResponse<T>(Stream stream, CancellationToken cancellationToken)
        where T : SessionResponse
    {
        var message = await StreamUtils.ReadMessageAsync(stream, cancellationToken).Vhc();
        try {
            var response = JsonUtils.Deserialize<T>(message);
            ProcessResponseException(response);
            return response;
        }
        // the response is already processed and exception is thrown
        catch (SessionException) {
            throw;
        }
        // try to deserialize as a SessionResponse (base)
        catch when (typeof(T) != typeof(SessionResponse)) {
            var sessionResponse = JsonUtils.Deserialize<SessionResponse>(message);
            ProcessResponseException(sessionResponse);
            throw;
        }
    }

    private static void ProcessResponseException(SessionResponse response)
    {
        if (response.ErrorCode == SessionErrorCode.RedirectHost)
            throw new RedirectHostException(response);

        if (response.ErrorCode == SessionErrorCode.Maintenance)
            throw new MaintenanceException();

        if (response.ErrorCode != SessionErrorCode.Ok)
            throw new SessionException(response);
    }

    private static EventId GetRequestEventId(ClientRequest request)
    {
        return (RequestCode)request.RequestCode switch {
            RequestCode.Hello => GeneralEventId.Session,
            RequestCode.Bye => GeneralEventId.Session,
            RequestCode.TcpPacketChannel => GeneralEventId.PacketChannel,
            RequestCode.ProxyChannel => GeneralEventId.ProxyChannel,
            _ => GeneralEventId.Request
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 0) {
            if (disposing) {
                _cancellationTokenSource.TryCancel(); // cancel all pending requests
                _cancellationTokenSource.Dispose(); // dispose the cancellation token source
            }
        }

        base.Dispose(disposing);
    }
}
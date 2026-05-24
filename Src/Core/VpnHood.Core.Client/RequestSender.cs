using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Client.Exceptions;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Streams;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Connections;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Client;

/// <summary>
/// Sends binary protocol requests to the VPN server over streams provided by <see cref="ConnectorService"/>.
/// </summary>
/// <remarks>
/// <see cref="RequestSender"/> is the sole owner of its <see cref="ConnectorService"/>: it creates one
/// from the supplied <see cref="ConnectorServiceOptions"/> and disposes it when it is itself disposed.
/// Callers that need to configure transport details (e.g. QUIC endpoint, connection reuse) access the
/// service via <see cref="ConnectorService"/>. Statistics are exposed through <see cref="ConnectorService.Stat"/>.
/// <para>
/// Request flow:
/// <list type="number">
///   <item>Try to reuse a free pooled stream from <see cref="ConnectorService.GetFreeConnection"/>.</item>
///   <item>On failure or no free connection, open a fresh stream via <see cref="ConnectorService.GetConnectionToServer"/>.</item>
///   <item>Serialize the request, write it to the stream, read back the <see cref="VpnHood.Core.Common.Messaging.SessionResponse"/>.</item>
/// </list>
/// </para>
/// Only TCP connections are added to the reuse pool. QUIC streams are not reused because each QUIC
/// stream is already a lightweight, independent channel within the same QUIC connection.
/// </remarks>
internal class RequestSender(ConnectorService connectorService) : IDisposable
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private int _isDisposed;

    public ConnectorService ConnectorService { get; } = connectorService;

    public Task<ConnectorRequestResult<T>> SendRequest<T>(ClientRequest request,
        CancellationToken cancellationToken)
        where T : SessionResponse
    {
        var requestEx = new ClientRequestEx { Request = request, PostBuffer = Memory<byte>.Empty };
        return SendRequest<T>(requestEx, cancellationToken);
    }

    public async Task<ConnectorRequestResult<T>> SendRequest<T>(ClientRequestEx requestEx,
        CancellationToken cancellationToken)
        where T : SessionResponse
    {
        var request = requestEx.Request;
        using var timeoutCts = new CancellationTokenSource(ConnectorService.RequestTimeout);
        using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutCts.Token, cancellationToken, _cancellationTokenSource.Token);

        // callback to reset timeout on each proxy attempt
        // ReSharper disable once AccessToDisposedClosure
        void OnAttempt() => timeoutCts.CancelAfter(ConnectorService.RequestTimeout);

        try {
            var eventId = GetRequestEventId(request);
            VhLogger.Instance.LogDebug(eventId,
                "Sending a request. RequestCode: {RequestCode}, RequestId: {RequestId}",
                (RequestCode)request.RequestCode, request.RequestId);

            // build the payload
            var requestBuffer = StreamUtils.ObjectToJsonBuffer(request);
            var payloadLength = 2 + requestBuffer.Length + requestEx.PostBuffer.Length;
            var payload = new byte[payloadLength];
            payload[0] = 1; // version
            payload[1] = request.RequestCode; // request code
            requestBuffer.Span.CopyTo(payload.AsSpan(2)); // request buffer
            requestEx.PostBuffer.Span.CopyTo(payload.AsSpan(2 + requestBuffer.Length)); // post buffer

            // ReSharper disable once AccessToDisposedClosure
            var ret = await SendRequest<T>(payload, request.RequestId, OnAttempt, requestCts.Token).Vhc();

            // log the response
            VhLogger.Instance.LogDebug(eventId, "Received a response... ErrorCode: {ErrorCode}.",
                ret.Response.ErrorCode);

            lock (ConnectorService.Stat) ConnectorService.Stat.RequestCount++;
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
        var reusableConnection = ConnectorService.GetFreeConnection();
        if (reusableConnection != null) {
            try {
                VhLogger.Instance.LogDebug(GeneralEventId.Stream,
                    "A shared Connection has been reused. ConnectionId: {ConnectionId}, LocalEp: {LocalEp}",
                    reusableConnection.ConnectionId, reusableConnection.LocalEndPoint);

                // send the request
                await reusableConnection.Stream.WriteAsync(request, cancellationToken).Vhc();
                var response = await ReadSessionResponse<T>(reusableConnection.Stream, cancellationToken).Vhc();
                lock (ConnectorService.Stat) ConnectorService.Stat.ReusedConnectionSucceededCount++;
                return new ConnectorRequestResult<T> {
                    Response = response,
                    StreamConnection = reusableConnection
                };
            }
            catch (SessionException) {
                // let the caller handle the exception. there is no error in connection
                await reusableConnection.DisposeAsync();
                throw;
            }
            catch (Exception ex) {
                // dispose the connection and retry with new connection
                lock (ConnectorService.Stat) ConnectorService.Stat.ReusedConnectionFailedCount++;
                reusableConnection.PreventReuse();
                await reusableConnection.DisposeAsync();
                VhLogger.Instance.LogError(GeneralEventId.Stream, ex,
                    "Error in reusing the Connection. Try a new connection. ConnectionId: {ConnectionId}, RequestId: {requestId}",
                    reusableConnection.ConnectionId, requestId);
            }
        }

        // create a new connection
        var connection = await ConnectorService.GetConnectionToServer(requestId + ":tunnel", request.Length,
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
                StreamConnection = connection
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
                connection.ConnectionId, requestId);

            // dispose the connection
            (connection as ReusableStreamConnection)?.PreventReuse();
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

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0) return;
        _cancellationTokenSource.TryCancel();
        _cancellationTokenSource.Dispose();
        ConnectorService.Dispose();
    }
}
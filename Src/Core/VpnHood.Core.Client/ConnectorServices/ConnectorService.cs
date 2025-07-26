using Microsoft.Extensions.Logging;
using System.Net;
using VpnHood.Core.Client.Exceptions;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.ClientStreams;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.Sockets;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Client.ConnectorServices;

internal class ConnectorService(
    ConnectorEndPointInfo endPointInfo,
    ISocketFactory socketFactory,
    TimeSpan requestTimeout,
    bool allowTcpReuse)
    : ConnectorServiceBase(endPointInfo, socketFactory, allowTcpReuse)
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private int _isDisposed;

    public TimeSpan RequestTimeout { get; private set; } = requestTimeout;

    public void Init(int protocolVersion, byte[]? serverSecret, TimeSpan requestTimeout, TimeSpan tcpReuseTimeout, bool useWebSocket)
    {
        RequestTimeout = requestTimeout;
        base.Init(protocolVersion, serverSecret, tcpReuseTimeout, useWebSocket && protocolVersion >= 9);
    }

    public async Task<ConnectorRequestResult<T>> SendRequest<T>(ClientRequest request, CancellationToken cancellationToken)
        where T : SessionResponse
    {
        using var timeoutCts = new CancellationTokenSource(RequestTimeout);
        using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutCts.Token, cancellationToken, _cancellationTokenSource.Token);

        var eventId = GetRequestEventId(request);
        request.RequestId += ":client";
        VhLogger.Instance.LogDebug(eventId,
            "Sending a request. RequestCode: {RequestCode}, RequestId: {RequestId}",
            (RequestCode)request.RequestCode, request.RequestId);

        await using var mem = new MemoryStream();
        mem.WriteByte(1);
        mem.WriteByte(request.RequestCode);
        await StreamUtils.WriteObjectAsync(mem, request, requestCts.Token).Vhc();
        var ret = await SendRequest<T>(mem.ToArray(), request.RequestId, requestCts.Token).Vhc();

        // log the response
        VhLogger.Instance.LogDebug(eventId, "Received a response... ErrorCode: {ErrorCode}.", ret.Response.ErrorCode);

        lock (Stat) Stat.RequestCount++;
        return ret;
    }

    private async Task<ConnectorRequestResult<T>> SendRequest<T>(ReadOnlyMemory<byte> request, string requestId,
        CancellationToken cancellationToken)
        where T : SessionResponse
    {
        // try reuse
        var clientStream = GetFreeClientStream();
        if (clientStream != null) {
            try {
                VhLogger.Instance.LogDebug(GeneralEventId.TcpLife,
                    "A shared ClientStream has been reused. ClientStreamId: {ClientStreamId}, LocalEp: {LocalEp}",
                    clientStream.ClientStreamId, clientStream.IpEndPointPair.LocalEndPoint);

                // send the request
                await clientStream.Stream.WriteAsync(request, cancellationToken).Vhc();
                var response = await ReadSessionResponse<T>(clientStream.Stream, cancellationToken).Vhc();
                lock (Stat) Stat.ReusedConnectionSucceededCount++;
                return new ConnectorRequestResult<T> {
                    Response = response,
                    ClientStream = clientStream
                };
            }
            catch (SessionException) {
                // let the caller handle the exception. there is no error in connection
                clientStream.Dispose();
                throw;
            }
            catch (Exception ex) {
                // dispose the connection and retry with new connection
                lock (Stat) Stat.ReusedConnectionFailedCount++;
                clientStream.DisposeWithoutReuse();
                VhLogger.Instance.LogError(GeneralEventId.TcpLife, ex,
                    "Error in reusing the ClientStream. Try a new connection. ClientStreamId: {ClientStreamId}, RequestId: {requestId}",
                    clientStream.ClientStreamId, requestId);
            }
        }

        // create a new connection
        clientStream = await GetTlsConnectionToServer(requestId + ":tunnel", request.Length, cancellationToken).Vhc();

        // send request
        try {
            // send the request
            await clientStream.Stream.WriteAsync(request, cancellationToken).Vhc();
            await clientStream.Stream.FlushAsync(cancellationToken);

            // parse the HTTP request
            if (clientStream.RequireHttpResponse) {
                clientStream.RequireHttpResponse = false;
                var responseMessage = await HttpUtil.ReadResponse(clientStream.Stream, cancellationToken).Vhc();
                if (responseMessage.StatusCode == HttpStatusCode.Unauthorized)
                    throw new UnauthorizedAccessException();

                // other error response will be handled by the SessionResponse
            }

            // read the response
            var response2 = await ReadSessionResponse<T>(clientStream.Stream, cancellationToken).Vhc();
            return new ConnectorRequestResult<T> {
                Response = response2,
                ClientStream = clientStream
            };
        }
        catch (SessionException) {
            // let the caller handle the exception. there is no error in connection
            clientStream.Dispose();
            throw;
        }
        catch (Exception ex){
            VhLogger.Instance.LogDebug(GeneralEventId.TcpLife, ex,
                "Error in sending fresh request. ClientStreamId: {ClientStreamId}, RequestId: {requestId}",
                clientStream.ClientStreamId, requestId);

            clientStream.DisposeWithoutReuse();
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
            _ => GeneralEventId.Tcp
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
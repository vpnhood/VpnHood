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
    TimeSpan tcpConnectTimeout,
    bool allowTcpReuse = true)
    : ConnectorServiceBase(endPointInfo, socketFactory, tcpConnectTimeout, allowTcpReuse)
{
    public async Task<ConnectorRequestResult<T>> SendRequest<T>(ClientRequest request,
        CancellationToken cancellationToken)
        where T : SessionResponse
    {
        var eventId = GetRequestEventId(request);
        request.RequestId += ":client";
        VhLogger.Instance.LogDebug(eventId,
            "Sending a request. RequestCode: {RequestCode}, RequestId: {RequestId}",
            (RequestCode)request.RequestCode, request.RequestId);

        // set request timeout
        using var localTimeoutCts = new CancellationTokenSource(RequestTimeout);
        using var localCts = CancellationTokenSource.CreateLinkedTokenSource(localTimeoutCts.Token, cancellationToken);

        await using var mem = new MemoryStream();
        mem.WriteByte(1);
        mem.WriteByte(request.RequestCode);
        await StreamUtils.WriteObjectAsync(mem, request, localCts.Token).VhConfigureAwait();
        var ret = await SendRequest<T>(mem.ToArray(), request.RequestId, localCts.Token).VhConfigureAwait();

        // log the response
        VhLogger.Instance.LogDebug(eventId, "Received a response... ErrorCode: {ErrorCode}.", ret.Response.ErrorCode);

        lock (Stat) Stat.RequestCount++;
        return ret;
    }

    private async Task<ConnectorRequestResult<T>> SendRequest<T>(byte[] request, string requestId,
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
                await clientStream.Stream.WriteAsync(request, cancellationToken).VhConfigureAwait();
                var response = await ReadSessionResponse<T>(clientStream.Stream, cancellationToken).VhConfigureAwait();
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
                VhLogger.LogError(GeneralEventId.TcpLife, ex,
                    "Error in reusing the ClientStream. Try a new connection. ClientStreamId: {ClientStreamId}",
                    clientStream.ClientStreamId);
            }
        }

        // create a new connection
        clientStream = await GetTlsConnectionToServer(requestId + ":tunnel", cancellationToken).VhConfigureAwait();

        // send request
        try {
            // send the request
            await clientStream.Stream.WriteAsync(request, cancellationToken).VhConfigureAwait();

            // parse the HTTP request
            if (clientStream.RequireHttpResponse) {
                clientStream.RequireHttpResponse = false;
                var responseMessage = await HttpUtil.ReadResponse(clientStream.Stream, cancellationToken).VhConfigureAwait();
                if (responseMessage.StatusCode == HttpStatusCode.Unauthorized)
                    throw new UnauthorizedAccessException();

                // other error response will be handled by the SessionResponse
            }

            // read the response
            var response2 = await ReadSessionResponse<T>(clientStream.Stream, cancellationToken).VhConfigureAwait();
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
        catch {
            clientStream.DisposeWithoutReuse();
            throw;
        }
    }

    private static async Task<T> ReadSessionResponse<T>(Stream stream, CancellationToken cancellationToken)
        where T : SessionResponse
    {
        var message = await StreamUtils.ReadMessageAsync(stream, cancellationToken).VhConfigureAwait();
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
}
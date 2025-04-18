﻿using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Exceptions;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
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
        VhLogger.Instance.LogDebug(eventId,
            "Sending a request. RequestCode: {RequestCode}, RequestId: {RequestId}",
            (RequestCode)request.RequestCode, request.RequestId);

        // set request timeout
        using var cancellationTokenSource = new CancellationTokenSource(RequestTimeout);
        using var linkedCancellationTokenSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationTokenSource.Token, cancellationToken);
        cancellationToken = linkedCancellationTokenSource.Token;

        await using var mem = new MemoryStream();
        mem.WriteByte(1);
        mem.WriteByte(request.RequestCode);
        await StreamUtils.WriteObjectAsync(mem, request, cancellationToken).VhConfigureAwait();
        var ret = await SendRequest<T>(mem.ToArray(), request.RequestId, cancellationToken).VhConfigureAwait();

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
            VhLogger.Instance.LogDebug(GeneralEventId.TcpLife,
                "A shared ClientStream has been reused. ClientStreamId: {ClientStreamId}, LocalEp: {LocalEp}",
                clientStream.ClientStreamId, clientStream.IpEndPointPair.LocalEndPoint);

            try {
                // we may use this buffer to encrypt so clone it for retry
                await clientStream.Stream.WriteAsync((byte[])request.Clone(), cancellationToken).VhConfigureAwait();
                var response = await ReadSessionResponse<T>(clientStream.Stream, cancellationToken).VhConfigureAwait();
                lock (Stat) Stat.ReusedConnectionSucceededCount++;
                return new ConnectorRequestResult<T> {
                    Response = response,
                    ClientStream = clientStream
                };
            }
            catch (SessionException) {
                // let the caller handle the exception. there is no error in connection
                throw;
            }
            catch (Exception ex) {
                // dispose the connection and retry with new connection
                lock (Stat) Stat.ReusedConnectionFailedCount++;
                DisposingTasks.Add(clientStream.DisposeAsync(false));
                VhLogger.LogError(GeneralEventId.TcpLife, ex,
                    "Error in reusing the ClientStream. Try a new connection. ClientStreamId: {ClientStreamId}",
                    clientStream.ClientStreamId);
            }
        }

        // create free connection
        clientStream = await GetTlsConnectionToServer(requestId, cancellationToken).VhConfigureAwait();

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
            throw;
        }
        catch {
            DisposingTasks.Add(clientStream.DisposeAsync(false));
            throw;
        }
    }

    private static async Task<T> ReadSessionResponse<T>(Stream stream, CancellationToken cancellationToken)
        where T : SessionResponse
    {
        var message = await StreamUtils.ReadMessage(stream, cancellationToken).VhConfigureAwait();
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
            RequestCode.TcpDatagramChannel => GeneralEventId.DatagramChannel,
            RequestCode.StreamProxyChannel => GeneralEventId.StreamProxyChannel,
            _ => GeneralEventId.Tcp
        };
    }
}
using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Streams;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels.Streams;
using VpnHood.Core.Tunneling.Connections;
using VpnHood.Core.Tunneling.WebSockets;

namespace VpnHood.Core.Server.Utils;

public static class ConnectionExtensions
{
    extension(IStreamConnection streamConnection)
    {
        public async Task WriteResponseAsync(SessionResponse sessionResponse,
            CancellationToken cancellationToken)
        {
            var responseData = StreamUtils.ObjectToJsonBuffer(sessionResponse);

            // If the client stream requires an HTTP response, write it to the client stream
            if (streamConnection.RequireHttpResponse) {
                streamConnection.RequireHttpResponse = false;
                await streamConnection.Stream.WriteAsync(HttpResponseBuilder.Ok(responseData.Length), cancellationToken).Vhc();
            }

            // Write the session response to the client stream
            await streamConnection.Stream.WriteAsync(responseData, cancellationToken).Vhc();
        }

        public async Task DisposeAsync(SessionResponse sessionResponse, CancellationToken cancellationToken)
        {
            // Write the session response to the client stream
            try {
                if (sessionResponse.ErrorCode == SessionErrorCode.Maintenance)
                    await WriteMaintenance(streamConnection, cancellationToken).Vhc();
                else
                    await streamConnection.WriteResponseAsync(sessionResponse, cancellationToken).Vhc();

                await streamConnection.Stream.FlushAsync(cancellationToken).Vhc();
            }
            catch (Exception ex) {
                VhLogger.Instance.LogDebug(GeneralEventId.Stream, ex,
                    "Could not dispose a Connection gracefully. ConnectionId: {ConnectionId}",
                    streamConnection.ConnectionId);
            }

            // the connection must never be left for the GC to collect: a finalized socket is closed
            // abortively, and the reset discards the response the client has not read yet.
            // reuse stays off here, by decision: a reply ends its connection, an Ok one included, so a client that
            // pools one after a reply opens a new one for its next request; keeping it is a separate decision
            streamConnection.PreventReuse();
            await streamConnection.DisposeAsync().Vhc();
        }
    }

    // a web server's own answers, never VpnHood's: a 503 while the HTTP answer is still to come, or a WebSocket's
    // "Try Again Later" close after its upgrade; a stream with neither only closes
    private static async Task WriteMaintenance(IStreamConnection streamConnection, CancellationToken cancellationToken)
    {
        if (streamConnection.RequireHttpResponse) {
            streamConnection.RequireHttpResponse = false;
            await streamConnection.Stream.WriteAsync(HttpResponseBuilder.ServiceUnavailable(), cancellationToken).Vhc();
        }
        else if (streamConnection.Stream is WebSocketStream webSocketStream)
            await webSocketStream.WriteClose(WebSocketUtils.CloseStatusTryAgainLater, cancellationToken).Vhc();
    }
}
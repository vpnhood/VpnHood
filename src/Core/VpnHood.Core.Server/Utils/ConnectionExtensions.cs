using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Streams;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Connections;

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
            // reuse stays off here: no reply path has ever disposed its connection, so server-side reuse of
            // a request connection has never actually run; turning it on is a separate decision
            streamConnection.PreventReuse();
            await streamConnection.DisposeAsync().Vhc();
        }
    }
}
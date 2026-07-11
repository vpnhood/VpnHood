using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Streams;
using VpnHood.Core.Toolkit.Utils;
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
            }
            catch (Exception ex) {
                VhLogger.Instance.LogDebug(GeneralEventId.Stream, ex,
                    "Could not dispose a Connection gracefully. ConnectionId: {ConnectionId}",
                    streamConnection.ConnectionId);

                streamConnection.Dispose();
            }
        }
    }
}
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
    extension(IConnection connection)
    {
        public async Task WriteResponseAsync(SessionResponse sessionResponse,
            CancellationToken cancellationToken)
        {
            var responseData = StreamUtils.ObjectToJsonBuffer(sessionResponse);

            // If the client stream requires an HTTP response, write it to the client stream
            if (connection.RequireHttpResponse) {
                connection.RequireHttpResponse = false;
                await connection.Stream.WriteAsync(HttpResponseBuilder.Ok(responseData.Length), cancellationToken).Vhc();
            }

            // Write the session response to the client stream
            await connection.Stream.WriteAsync(responseData, cancellationToken).Vhc();
        }

        public async Task DisposeAsync(SessionResponse sessionResponse, CancellationToken cancellationToken)
        {
            // Write the session response to the client stream
            try {
                await connection.WriteResponseAsync(sessionResponse, cancellationToken).Vhc();
            }
            catch (Exception ex) {
                VhLogger.Instance.LogDebug(GeneralEventId.Stream, ex,
                    "Could not dispose a Connection gracefully. ConnectionId: {ConnectionId}",
                    connection.ConnectionId);

                connection.Dispose();
            }
        }
    }
}
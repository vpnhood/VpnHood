using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.ClientStreams;

namespace VpnHood.Core.Server.Utils;

public static class ClientStreamExtensions
{
    public static async Task WriteResponseAsync(this IClientStream clientStream, SessionResponse sessionResponse,
        CancellationToken cancellationToken)
    {
        var responseData = StreamUtils.ObjectToJsonBuffer(sessionResponse);

        // If the client stream requires an HTTP response, write it to the client stream
        if (clientStream.RequireHttpResponse) {
            clientStream.RequireHttpResponse = false;
            await clientStream.Stream.WriteAsync(HttpResponseBuilder.Ok(responseData.Length), cancellationToken).Vhc();
        }

        // Write the session response to the client stream
        await clientStream.Stream.WriteAsync(responseData, cancellationToken).Vhc();
    }

    public static async Task DisposeAsync(this IClientStream clientStream, SessionResponse sessionResponse, CancellationToken cancellationToken)
    {
        // Write the session response to the client stream
        await clientStream.WriteResponseAsync(sessionResponse, cancellationToken).Vhc();
        clientStream.Dispose();
    }
}
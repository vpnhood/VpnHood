using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.ClientStreams;

namespace VpnHood.Core.Server.Utils;

public static class ClientStreamExtensions
{
    public static async Task WriteResponse(this IClientStream clientStream, SessionResponse sessionResponse,
        CancellationToken cancellationToken)
    {
        // If the client stream requires an HTTP response, write it to the client stream
        if (clientStream.RequireHttpResponse) {
            clientStream.RequireHttpResponse = false;
            await clientStream.Stream.WriteAsync(HttpResponseBuilder.Ok(), cancellationToken).VhConfigureAwait();
        }

        // Write the session response to the client stream
        await StreamUtils.WriteObjectAsync(clientStream.Stream, sessionResponse, cancellationToken).VhConfigureAwait();
    }

    public static async Task WriteFinalResponse(this IClientStream clientStream, SessionResponse sessionResponse,
        CancellationToken cancellationToken)
    {
        // Write the session response to the client stream
        await clientStream.WriteResponse(sessionResponse, cancellationToken).VhConfigureAwait();
        await clientStream.DisposeAsync().VhConfigureAwait();
    }

    public static async Task WriteFinalResponseUngracefully(this IClientStream clientStream,
        SessionResponse sessionResponse, CancellationToken cancellationToken)
    {
        // Write the session response to the client stream
        await clientStream.WriteResponse(sessionResponse, cancellationToken).VhConfigureAwait();
        await clientStream.DisposeAsync(false).VhConfigureAwait();
    }
}
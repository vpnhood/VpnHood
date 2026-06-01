namespace VpnHood.Core.Client.VpnServices.Abstractions;

// Processes a single API request read from inputStream and writes the response to outputStream.
// Implemented by the VpnService host (ApiController); invoked by an IVpnServiceApiListener.
public interface IVpnServiceApiRequestHandler
{
    Task ProcessRequestAsync(Stream inputStream, Stream outputStream, CancellationToken cancellationToken);
}

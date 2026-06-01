using System.Net;

namespace VpnHood.Core.Client.VpnServices.Abstractions;

// Server-side transport for the VpnService API. Encapsulates how the host receives requests
// (e.g. TCP loopback or platform native messages) and forwards them to an IVpnServiceApiRequestHandler.
public interface IVpnServiceApiListener : IDisposable
{
    // The endpoint clients connect to, or null for transports without a network endpoint (e.g. message-based).
    IPEndPoint? ApiEndPoint { get; }

    // The key clients must present, or null for transports that don't use a key.
    byte[]? ApiKey { get; }

    // Begin listening; each received request is forwarded to the given handler.
    void Start(IVpnServiceApiRequestHandler requestHandler);
}

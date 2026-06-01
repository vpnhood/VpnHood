using System.Net;
using VpnHood.Core.Client.VpnServices.Abstractions;

namespace VpnHood.Core.Client.VpnServices.Host;

// VpnService API transport that exchanges raw request/response byte buffers instead of using a network socket.
// Used by platforms with a native IPC channel (e.g. iOS provider messages). The host has no endpoint or key;
// the platform routes each message into ProcessMessageAsync.
public sealed class MessageVpnServiceApiListener : IVpnServiceApiListener
{
    private IVpnServiceApiRequestHandler? _requestHandler;

    public IPEndPoint? ApiEndPoint => null;
    public byte[]? ApiKey => null;

    public void Start(IVpnServiceApiRequestHandler requestHandler)
    {
        _requestHandler = requestHandler;
    }

    public async Task<byte[]> ProcessMessageAsync(byte[] requestBytes, CancellationToken cancellationToken)
    {
        var requestHandler = _requestHandler ?? throw new InvalidOperationException("Listener has not been started.");
        await using var inputStream = new MemoryStream(requestBytes, writable: false);
        await using var outputStream = new MemoryStream();
        await requestHandler.ProcessRequestAsync(inputStream, outputStream, cancellationToken);
        return outputStream.ToArray();
    }

    public void Dispose()
    {
    }
}

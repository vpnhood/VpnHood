using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;
using VpnHood.Core.Toolkit.Extensions;

namespace VpnHood.Core.Client.Devices.Ios;

// iOS IMessageListener. The iOS Network Extension receives app messages through
// NEPacketTunnelProvider.HandleAppMessage (a private, secure channel) rather than a TCP
// socket, so there is no endpoint or API key to manage. The provider forwards each message
// to ProcessMessageAsync, which dispatches to the stored handler.
public sealed class IosMessageListener : IMessageListener
{
    private MessageHandler? _messageHandler;

    public Task Start(MessageHandler messageHandler, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        _messageHandler = messageHandler;
        return Task.CompletedTask;
    }

    // Invoked by IosVpnService.HandleAppMessage for each incoming app message.
    public async Task<byte[]> ProcessMessageAsync(byte[] request, CancellationToken cancellationToken)
    {
        var handler = _messageHandler ??
                      throw new InvalidOperationException("VpnService message listener is not started.");

        var response = await handler(request, cancellationToken).Vhc();
        return response.ToArray();
    }

    public void Dispose()
    {
        _messageHandler = null;
    }
}

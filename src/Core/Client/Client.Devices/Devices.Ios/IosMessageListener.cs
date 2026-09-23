using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.Core.Client.Devices.Ios;

// iOS IMessageListener. The iOS Network Extension receives app messages through
// NEPacketTunnelProvider.HandleAppMessage (a private, secure channel) rather than a TCP socket, so
// there is no endpoint or API key to manage. A pure transport: response shaping (ApiResponse /
// ApiError) is owned by the handler (ApiController), which never throws. When dispatch itself is
// impossible (a message arrives before the host has started), it answers nil — the app-side
// IosMessageClient surfaces that as VpnServiceUnreachableException, the same semantics as a refused
// TCP connection on other platforms.
public sealed class IosMessageListener : IMessageListener
{
    private MessageHandler? _messageHandler;

    public Task Start(MessageHandler messageHandler, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        _messageHandler = messageHandler;
        return Task.CompletedTask;
    }

    // Invoked by IosVpnService.HandleAppMessage for each incoming app message. Never throws and
    // always invokes the completion handler, or the app-side RPC would hang until its timeout.
    public async Task ProcessAppMessage(NSData? messageData, Action<NSData>? completionHandler)
    {
        using var responseData = await TryGetResponse(messageData).Vhc();

        // the ObjC completion block accepts nil even though the binding's Action<NSData> does not
        try { completionHandler?.Invoke(responseData!); } catch { /* ignore */ }
    }

    // Returns the handler's response, or null when the message cannot be dispatched (host not
    // started yet, empty message) or the handler fails — nil tells the app the service is unreachable.
    private async Task<NSData?> TryGetResponse(NSData? messageData)
    {
        try {
            var handler = _messageHandler;
            if (handler == null || messageData == null)
                return null;

            var responseBytes = await handler(messageData.ToArray(), CancellationToken.None).Vhc();
            return NSData.FromArray(responseBytes.ToArray());
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not process an app message.");
            return null;
        }
    }

    public void Dispose()
    {
        _messageHandler = null;
    }
}

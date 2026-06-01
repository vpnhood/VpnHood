using NetworkExtension;
using VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;

namespace VpnHood.Core.Client.Device.iOS;

// iOS IMessageClient. Sends opaque request blobs to the Network Extension over
// NETunnelProviderSession.SendProviderMessage (a private, secure channel) and returns the
// raw response blob. Framing and serialization are handled by the caller / ApiController.
internal sealed class ProviderMessageClient(NETunnelProviderManager vpnManager) : IMessageClient
{
    public async Task<Memory<byte>> SendAsync(Memory<byte> request, CancellationToken cancellationToken)
    {
        if (vpnManager.Connection is not NETunnelProviderSession providerSession)
            throw new VpnServiceUnreachableException("VpnService is unreachable.",
                new InvalidOperationException("NETunnelProviderSession is not available."));

        var responseDataTask = new TaskCompletionSource<NSData?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var cancellationRegistration = cancellationToken.Register(
            () => responseDataTask.TrySetCanceled(cancellationToken));

        if (!providerSession.SendProviderMessage(NSData.FromArray(request.ToArray()), out var sendError,
            responseData => responseDataTask.TrySetResult(responseData))) {
            throw new VpnServiceUnreachableException("VpnService is unreachable.",
                sendError == null ? null : new Exception(sendError.LocalizedDescription));
        }

        var responseData = await responseDataTask.Task.ConfigureAwait(false);
        if (responseData == null)
            throw new VpnServiceUnreachableException("VpnService is unreachable.",
                new InvalidOperationException("Provider response was null."));

        return responseData.ToArray();
    }

    public void Dispose()
    {
    }
}

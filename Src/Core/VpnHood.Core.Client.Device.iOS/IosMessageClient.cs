using NetworkExtension;
using VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;

namespace VpnHood.Core.Client.Device.iOS;

// iOS IMessageClient. Sends opaque request blobs to the Network Extension over
// NETunnelProviderSession.SendProviderMessage (a private, secure channel) and returns the
// raw response blob. Framing and serialization are handled by the caller / ApiController.
internal sealed class IosMessageClient(NETunnelProviderManager vpnManager) : IMessageClient
{
    public async Task<Memory<byte>> SendAsync(Memory<byte> request, CancellationToken cancellationToken)
    {
        var providerSession = await GetProviderSessionAsync(cancellationToken);

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

    // Resolve the live provider session to the running Network Extension.
    //
    // The extension keeps the VPN up after the app is untasked/killed. When the app relaunches it
    // gets a FRESH NETunnelProviderManager that is NOT bound to the running configuration, so its
    // Connection is not a session and the channel looks "unreachable". Apple's supported way to talk
    // to an already-running provider is to enumerate the SAVED managers via LoadAllFromPreferences
    // and use the one whose Connection is the live NETunnelProviderSession (and sendProviderMessage
    // even relaunches the provider if needed). So: try the app's own manager first (covers the case
    // where THIS app started the tunnel); otherwise fall back to LoadAllFromPreferences.
    private async Task<NETunnelProviderSession> GetProviderSessionAsync(CancellationToken cancellationToken)
    {
        if (vpnManager.Connection is NETunnelProviderSession ownSession && ownSession.Status != NEVpnStatus.Invalid)
            return ownSession;

        NSArray? managersArray = null;
        try { managersArray = await NETunnelProviderManager.LoadAllFromPreferencesAsync(); }
        catch { /* ignore */ }

        var session = managersArray == null
            ? null
            : NSArray.FromArray<NETunnelProviderManager>(managersArray)
                .Select(m => m.Connection as NETunnelProviderSession)
                .FirstOrDefault(s => s != null && s.Status is NEVpnStatus.Connected or NEVpnStatus.Connecting or NEVpnStatus.Reasserting or NEVpnStatus.Disconnecting)
                ?? NSArray.FromArray<NETunnelProviderManager>(managersArray)
                .Select(m => m.Connection as NETunnelProviderSession)
                .FirstOrDefault(s => s != null);

        if (session != null)
            return session;

        throw new VpnServiceUnreachableException("VpnService is unreachable.",
            new InvalidOperationException("NETunnelProviderSession is not available."));
    }

    public void Dispose()
    {
    }
}

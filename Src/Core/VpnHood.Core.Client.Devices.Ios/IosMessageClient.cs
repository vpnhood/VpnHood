using NetworkExtension;
using VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;

namespace VpnHood.Core.Client.Devices.Ios;

// iOS IMessageClient. Sends opaque request blobs to the Network Extension over
// NETunnelProviderSession.SendProviderMessage (a private, secure channel) and returns the
// raw response blob. Framing and serialization are handled by the caller / ApiController.
//
// Mirrors the cross-platform TcpMessageClient model: resolve the live provider session ONCE and
// reuse it for every subsequent poll, re-resolving only when the cached session dies or a send
// fails. The previous implementation called LoadAllFromPreferencesAsync on every 1 s status poll
// (heavy + racy) and fell back to messaging Disconnected/Invalid sessions, which woke dead
// extensions and surfaced a false "Connected" state after a jetsam kill.
internal sealed class IosMessageClient(NETunnelProviderManager vpnManager) : IMessageClient
{
    // Last known-live session; reused across polls so we avoid a LoadAllFromPreferences round trip
    // each second. Invalidated (set null) whenever a send fails so the next call re-resolves it.
    private NETunnelProviderSession? _session;

    public async Task<Memory<byte>> SendAsync(Memory<byte> request, CancellationToken cancellationToken)
    {
        var providerSession = await GetProviderSessionAsync(cancellationToken);

        var responseDataTask = new TaskCompletionSource<NSData?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var cancellationRegistration = cancellationToken.Register(
            () => responseDataTask.TrySetCanceled(cancellationToken));

        if (!providerSession.SendProviderMessage(NSData.FromArray(request.ToArray()), out var sendError,
            responseData => responseDataTask.TrySetResult(responseData))) {
            _session = null; // drop the stale session so the next call re-resolves it
            throw new VpnServiceUnreachableException("VpnService is unreachable.",
                sendError == null ? null : new Exception(sendError.LocalizedDescription));
        }

        var responseData = await responseDataTask.Task.ConfigureAwait(false);
        if (responseData == null) {
            _session = null;
            throw new VpnServiceUnreachableException("VpnService is unreachable.",
                new InvalidOperationException("Provider response was null."));
        }

        return responseData.ToArray();
    }

    // A session we can actually message: the extension process is up (or coming up / going down),
    // never Disconnected or Invalid.
    private static bool IsLive(NETunnelProviderSession? session) =>
        session is {
            Status: NEVpnStatus.Connected or NEVpnStatus.Connecting
                or NEVpnStatus.Reasserting or NEVpnStatus.Disconnecting
        };

    // Resolve the live provider session to the running Network Extension.
    //
    // The extension keeps the VPN up after the app is untasked/killed. When the app relaunches it
    // gets a FRESH NETunnelProviderManager that is NOT bound to the running configuration, so its
    // Connection is not a live session. Apple's supported way to reach an already-running provider
    // is to enumerate the SAVED managers via LoadAllFromPreferences and use the one whose Connection
    // is a live NETunnelProviderSession. We cache that session and reuse it for later polls.
    private async Task<NETunnelProviderSession> GetProviderSessionAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        // 1. Reuse the cached live session (connection-reuse, like TcpMessageClient).
        if (IsLive(_session))
            return _session!;

        // 2. The app's own manager — authoritative when THIS app started the tunnel.
        if (vpnManager.Connection is NETunnelProviderSession ownSession && IsLive(ownSession))
            return _session = ownSession;

        // 3. App relaunched while the extension kept running: find the live saved session.
        // Do NOT fall back to a Disconnected/Invalid session: messaging one can wake a dead extension
        // or return stale data (the false-"Connected"-at-launch bug). Fail fast so the manager
        // transitions to Disposed/None on the next poll.
        try {
            var managersArray = await NETunnelProviderManager.LoadAllFromPreferencesAsync();
            var liveSessions = managersArray.ToArray<NETunnelProviderManager>()
                .Select(m => m?.Connection as NETunnelProviderSession)
                .Where(IsLive);
                
            var liveSession = liveSessions.FirstOrDefault() 
                ?? throw new VpnServiceUnreachableException("VpnService is unreachable. No live provider sessions found.");
              
            return liveSession; 
        }
        catch (Exception ex) {
            _session = null;
            throw new VpnServiceUnreachableException("VpnService is unreachable.", ex);
        }
    }

    public void Dispose()
    {
        _session = null;
    }
}

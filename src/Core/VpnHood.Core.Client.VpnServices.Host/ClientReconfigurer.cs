using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Client.VpnServices.Host;

// Reconfigures, one at a time, as the app sends them. The latest that came before the session was up is
// kept, and the connect applies it once it is. Each carries every live setting, so the latest is all
// there is to keep - but for the proxies' reset, which the app sends once: a newer one drops it.
internal class ClientReconfigurer(VpnServiceHost vpnServiceHost)
{
    private readonly AsyncLock _lock = new();
    private ClientReconfigureParams? _missedReconfigureParams;

    // A connect's start, before it reads the options file, which holds a kept one.
    public async Task Reset(CancellationToken cancellationToken)
    {
        using var scope = await _lock.LockAsync(cancellationToken).Vhc();
        _missedReconfigureParams = null;
    }

    // A reconfigure's settings, onto the session at once. One that comes before the session is up is kept
    // for the connect, which applies it once it is.
    public async Task Reconfigure(ClientReconfigureParams reconfigureParams, CancellationToken cancellationToken)
    {
        using var scope = await _lock.LockAsync(cancellationToken).Vhc();
        var client = vpnServiceHost.Client;
        if (client?.Session == null) {
            // the usage-data switch at once where there is a client: it tracks while it connects
            client?.Tracker?.IsEnabled = reconfigureParams.AllowAnonymousTracker;
            _missedReconfigureParams = reconfigureParams;
            return;
        }

        // this one is newer than a kept one
        _missedReconfigureParams = null;
        await ReconfigureClient(client, reconfigureParams, cancellationToken).Vhc();
    }

    // The kept one, onto the session of the connect that made this client; not once another connect has
    // replaced it, as one kept since is that connect's.
    public async Task ApplyMissed(VpnHoodClient client, CancellationToken cancellationToken)
    {
        using var scope = await _lock.LockAsync(cancellationToken).Vhc();
        if (vpnServiceHost.Client != client || _missedReconfigureParams is not { } missedReconfigureParams)
            return;

        _missedReconfigureParams = null;
        await VhInvoker.TryInvokeAsync("apply a reconfigure that came before the session", LogLevel.Warning,
            () => ReconfigureClient(client, missedReconfigureParams, cancellationToken)).Vhc();
    }

    private static async Task ReconfigureClient(VpnHoodClient client, ClientReconfigureParams reconfigureParams,
        CancellationToken cancellationToken)
    {
        client.UseTcpProxy = reconfigureParams.UseTcpProxy;
        client.DropUdp = reconfigureParams.DropUdp;
        client.DropQuic = reconfigureParams.DropQuic;
        client.ChannelProtocol = reconfigureParams.ChannelProtocol;
        client.UnroutedIpMode = reconfigureParams.UnroutedIpMode;
        client.UnsupportedIpV6Mode = reconfigureParams.UnsupportedIpV6Mode;

        // the usage-data switch, at once: the session's tracker was made with the one at the connect
        if (client.Tracker != null)
            client.Tracker.IsEnabled = reconfigureParams.AllowAnonymousTracker;

        // live-swap the split filter gates to the current folder manifests (rewritten by the app before
        // this request); no-op when the db paths did not change
        client.NetFilter.Reconfigure(cancellationToken);

        if (client.ProxyConnector != null)
            await client.ProxyConnector.UpdateOptions(reconfigureParams.ProxyOptions, cancellationToken).Vhc();
    }
}

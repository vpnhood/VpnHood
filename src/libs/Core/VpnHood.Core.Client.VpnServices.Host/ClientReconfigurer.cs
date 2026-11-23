using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Proxies.Management.Abstractions.Options;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Client.VpnServices.Host;

// Reconfigures, one at a time, as the app sends them. The latest that came before the session was up is
// kept, and the connect applies it once it is. Each carries every live setting, so the latest is all
// there is to keep - but for the proxies' reset, which the app sends once: a newer one takes it on. One is kept until
// it is applied, so the reset of one whose apply failed goes on with the next too.
internal class ClientReconfigurer(VpnServiceHost vpnServiceHost)
{
    private readonly AsyncLock _lock = new();
    private ClientReconfigureParams? _missedReconfigureParams;

    // the reconfigure kept, where a test looks
    internal ClientReconfigureParams? KeptParams => _missedReconfigureParams;

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

        // a kept one's reset goes on with this one, whichever way this one goes, and it is kept before its steps below,
        // which can fail: the app sends a reset once
        reconfigureParams = WithKeptReset(_missedReconfigureParams, reconfigureParams);
        _missedReconfigureParams = reconfigureParams;

        // the kill switch at once, a session's or a start's: the block is up before the session
        await vpnServiceHost.ReconfigureKillSwitch(reconfigureParams.UseKillSwitch, cancellationToken).Vhc();

        var client = vpnServiceHost.Client;
        if (client?.Session == null) {
            // the usage-data switch at once where there is a client: it tracks while it connects
            client?.Tracker?.IsEnabled = reconfigureParams.AllowAnonymousTracker;
            return;
        }

        await ReconfigureClient(client, reconfigureParams, cancellationToken).Vhc();
        _missedReconfigureParams = null;
    }

    // The kept one, onto the session of the connect that made this client; not once another connect has
    // replaced it, as one kept since is that connect's. One that fails stays kept for the next reconfigure.
    public async Task ApplyMissed(VpnHoodClient client, CancellationToken cancellationToken)
    {
        using var scope = await _lock.LockAsync(cancellationToken).Vhc();
        if (vpnServiceHost.Client != client || _missedReconfigureParams is not { } missedReconfigureParams)
            return;

        try {
            await ReconfigureClient(client, missedReconfigureParams, cancellationToken).Vhc();
            _missedReconfigureParams = null;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not apply a reconfigure that came before the session.");
        }
    }

    // The reconfigure with the proxies' reset of the one kept, where it carries none of its own: the app sends a
    // reset once
    internal static ClientReconfigureParams WithKeptReset(ClientReconfigureParams? keptParams,
        ClientReconfigureParams reconfigureParams)
    {
        if (keptParams?.ProxyOptions.ResetStates == true && !reconfigureParams.ProxyOptions.ResetStates)
            reconfigureParams.ProxyOptions = WithResetStates(reconfigureParams.ProxyOptions);

        return reconfigureParams;
    }

    // the options with the reset, the rest as they are: copied by their JSON, as their properties are init-only
    private static ProxyOptions WithResetStates(ProxyOptions proxyOptions)
    {
        var options = JsonSerializer.SerializeToNode(proxyOptions) as JsonObject ??
                      throw new InvalidOperationException("The proxies' options made no JSON object.");

        options[nameof(ProxyOptions.ResetStates)] = true;
        return options.Deserialize<ProxyOptions>() ??
               throw new InvalidOperationException("The proxies' options could not be copied.");
    }

    private static async Task ReconfigureClient(VpnHoodClient client, ClientReconfigureParams reconfigureParams,
        CancellationToken cancellationToken)
    {
        client.UseTcpForwarding = reconfigureParams.UseTcpForwarding;
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
        await client.NetFilter.Reconfigure(cancellationToken).Vhc();

        if (client.ProxyConnector != null)
            await client.ProxyConnector.UpdateOptions(reconfigureParams.ProxyOptions, cancellationToken).Vhc();
    }
}

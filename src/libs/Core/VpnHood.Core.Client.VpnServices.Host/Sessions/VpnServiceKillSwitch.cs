using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Net.VpnAdapters.Abstractions;

namespace VpnHood.Core.Client.VpnServices.Host.Sessions;

// The kill switch's block for the host's sessions, one change at a time. A connect puts it up before its adapter, each
// start of the adapter points it at the tunnel, and a reconfigure turns it on or off. A session's end only stops that
// session's changes: lifting the block is the app's call. A connect that a newer one took over starts no change; one
// already running finishes, and the newer connect's own steps come after it.
internal class VpnServiceKillSwitch(IVpnKillSwitch? killSwitch, VpnServiceTunnel tunnel, VpnServiceContext context)
{
    private readonly AsyncLock _lock = new();
    private bool _useKillSwitch;
    private bool _isApplied; // whether the block is as _useKillSwitch says: not after a change that failed
    private VpnKillSwitchRules? _rules; // the current connect's, while it may change the block; null once it may not
    private bool _isStarting; // a connect is between BeginStart and Start
    private bool? _startUseKillSwitch; // the last switch asked for meanwhile, which Start takes over the options'

    // Called before a connect reads its options: a switch asked for from here until Start is kept for Start, not lost.
    // For a reconnect too, whose rules are still the last session's until Start
    public async Task BeginStart(int sessionId, CancellationToken cancellationToken)
    {
        using var scope = await _lock.LockAsync(cancellationToken).Vhc();
        ThrowIfTakenOver(sessionId);
        _isStarting = true;
        _startUseKillSwitch = null;
    }

    // Sets a connect's block before its adapter and first packet, by the last switch asked for since BeginStart, or
    // else the options', and returns the switch in use. The tunnel is this host's now, so a block that a crashed or
    // revoked holder left is replaced or removed. Fail-closed: a block that cannot go up fails the connect
    public async Task<bool> Start(int sessionId, ClientOptions clientOptions, CancellationToken cancellationToken)
    {
        using var scope = await _lock.LockAsync(cancellationToken).Vhc();
        ThrowIfTakenOver(sessionId);

        // a tunnel this connect took and no longer holds: the block would not be this host's to set
        if (!clientOptions.UseNullCapture && !tunnel.IsHeld)
            throw new VpnServiceRevokedException();

        var useKillSwitch = _startUseKillSwitch ?? clientOptions.UseKillSwitch;
        _isStarting = false;
        _startUseKillSwitch = null;
        if (useKillSwitch) {
            if (killSwitch == null)
                throw new NotSupportedException("This device has no kill switch.");

            // nothing would let the tunnel's traffic through: a null-capture makes no adapter of its own
            if (clientOptions.UseNullCapture)
                throw new NotSupportedException("The kill switch cannot work with a null-capture.");
        }

        _useKillSwitch = useKillSwitch;
        _rules = clientOptions.UseNullCapture
            ? null
            : VpnKillSwitchRules.CreateBySettings(clientOptions.AdapterName, clientOptions.IncludeIpRangesByDevice,
                clientOptions.SplitLocalNetwork);

        _isApplied = false;
        await Apply(_useKillSwitch, cancellationToken).Vhc();
        _isApplied = true;
        return useKillSwitch;
    }

    // Points the block at the adapter after each of its starts, as a restart may make a new interface, with what the
    // session's routes leave out. Until then the tunnel's own traffic is blocked too, which fails closed. The session
    // is checked once it has the lock: a newer connect may have begun, whose block the old routes would misdirect
    public async Task Repoint(int sessionId, VpnAdapterOptions adapterOptions, CancellationToken cancellationToken)
    {
        using var scope = await _lock.LockAsync(cancellationToken).Vhc();
        if (_rules == null || context.SessionId != sessionId)
            return;

        _rules = CreateRules(_rules.AdapterName, adapterOptions);
        if (!_useKillSwitch)
            return;

        _isApplied = false;
        await Apply(_useKillSwitch, cancellationToken).Vhc();
        _isApplied = true;
    }

    // The app turns the switch on or off. Between BeginStart and Start it is kept for Start. With no connect that may
    // change the block it does nothing: the app lifts the block itself. A device with no kill switch fails "on", as a
    // connect does. After an apply that failed the block's state is unknown (an unblock may have half worked), so the
    // next request is applied even if it asks for the setting already remembered
    public async Task Reconfigure(bool useKillSwitch, CancellationToken cancellationToken)
    {
        using var scope = await _lock.LockAsync(cancellationToken).Vhc();
        if (_rules == null && !_isStarting)
            return;

        if (useKillSwitch && killSwitch == null)
            throw new NotSupportedException("This device has no kill switch.");

        if (_isStarting) {
            _startUseKillSwitch = useKillSwitch;
            return;
        }

        if (_useKillSwitch == useKillSwitch && _isApplied)
            return;

        _isApplied = false;
        await Apply(useKillSwitch, cancellationToken).Vhc();
        _useKillSwitch = useKillSwitch;
        _isApplied = true;
    }

    // Stops a session's changes to the block, before the app hears of its end: the app may lift the block then, and no
    // change of this session may land after that. Does nothing for a session that a newer connect replaced
    public async Task EndChanges(int sessionId)
    {
        using var scope = await _lock.LockAsync(CancellationToken.None).Vhc();
        if (context.SessionId != sessionId)
            return;

        _rules = null;
        _isStarting = false;
        _startUseKillSwitch = null;
    }

    // under the lock
    private async Task Apply(bool useKillSwitch, CancellationToken cancellationToken)
    {
        if (killSwitch == null || !tunnel.IsHeld)
            return;

        if (useKillSwitch && _rules != null) {
            VhLogger.Instance.LogInformation("The kill switch blocks all but the tunnel. PassIpRanges: {Count}",
                _rules.PassIpRanges.Count);

            await killSwitch.Block(_rules, cancellationToken).Vhc();
            return;
        }

        await killSwitch.Unblock(anyApp: true, cancellationToken).Vhc();
    }

    // an adapter's start: what its routes leave out, as the adapter has them
    private static VpnKillSwitchRules CreateRules(string adapterName, VpnAdapterOptions adapterOptions)
    {
        var includeIpRanges = adapterOptions.IncludeNetworks.ToIpRanges();
        var passIpRanges = includeIpRanges.Invert();
        if (adapterOptions.ServerIp != null)
            passIpRanges = passIpRanges.Exclude(adapterOptions.ServerIp);

        return new VpnKillSwitchRules {
            AdapterName = adapterName,
            PassIpRanges = passIpRanges,
            DnsServers = adapterOptions.DnsServers.Where(x => !includeIpRanges.Contains(x)).ToArray()
        };
    }

    // a connect's steps under the lock, which a newer connect's own steps take after it has begun
    private void ThrowIfTakenOver(int sessionId)
    {
        if (context.SessionId != sessionId)
            throw new OperationCanceledException("A newer connect has taken over the VPN service.");
    }
}

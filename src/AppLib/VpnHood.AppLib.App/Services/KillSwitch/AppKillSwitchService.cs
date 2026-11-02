using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.Settings;
using VpnHood.AppLib.App.Settings;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppLib.App.Services.KillSwitch;

// The kill switch as the app keeps it, where the device has one (a desktop's IVpnKillSwitch). While a session runs,
// the block is the VPN service's, put up at its connect; this keeps what lies around sessions. The block stays
// through an error stop, a crash and a reboot, and only a person's disconnect lifts it, or the switch turned off
// (owner, 2026-10-31: "the block should stay until the user disconnects manually"). Lockdown keeps it while
// disconnected too (owner, 2026-11-01: an option, off by default, as a hotel's Wi-Fi sign-in needs it off): up by
// the settings' split at a person's disconnect, when the option is turned on and at the app's start. Whether this
// app's block is up is the OS's word, read at each of those and at each session's end, never per state read: on
// Linux a read runs nft.
internal sealed class AppKillSwitchService(VpnHoodApp app, IVpnKillSwitch killSwitch)
{
    private readonly AsyncLock _lock = new(); // this app's changes of the block, and its reads of it
    private volatile bool _isBlocking;

    // The kill switch as a connect runs with it: the person's, where the plan allows it.
    public bool IsOn => app.UserSettings.KillSwitch && app.IsPremiumFeatureAllowed(AppFeature.KillSwitch);

    // This app's block up with no session; a session's is its connect's, which the state shows already.
    public bool IsBlocking => _isBlocking && app.IsSessionless;

    private bool IsLockdownOn => IsOn && app.UserSettings.KillSwitchLockdown;

    // The block as the OS kept it, through a crash and a reboot; one kept while disconnected is put back too, as
    // another app's disconnect may have lifted it.
    public Task Start()
    {
        return VhInvoker.TryInvokeAsync("start the kill switch", LogLevel.Error, () => IsLockdownOn
            ? UpdateLockdown(CancellationToken.None)
            : Read(CancellationToken.None));
    }

    // Turned off, the block goes at once, one an error stop left too, which no session holds; the reconfigure takes
    // it from a session. Turned on, the reconfigure puts a session's up. Kept while disconnected, the block with no
    // session follows the settings: up at once, by the split as it is now, and lifted when that is turned off.
    public void ApplySettings(UserSettings oldUserSettings)
    {
        var userSettings = app.UserSettings;
        if (oldUserSettings.KillSwitch && !userSettings.KillSwitch)
            _ = VhInvoker.TryInvokeAsync("lift the kill switch's block", LogLevel.Error,
                () => Unblock(CancellationToken.None));
        else if (userSettings.KillSwitch && (userSettings.KillSwitchLockdown || oldUserSettings.KillSwitchLockdown))
            _ = VhInvoker.TryInvokeAsync("keep the kill switch's block while disconnected", LogLevel.Error,
                () => UpdateLockdown(CancellationToken.None));
    }

    // Whether the session left its block up, an error stop's; off the caller's lock, as the OS is asked.
    public void SessionEnded()
    {
        Task.Run(() => VhInvoker.TryInvokeAsync("read the kill switch's block", LogLevel.Warning,
            () => Read(CancellationToken.None)));
    }

    // A person's disconnect lifts this app's block, even when the stop failed; kept while disconnected, the block
    // stays, by the settings' split.
    public Task PersonDisconnected(CancellationToken cancellationToken)
    {
        return IsLockdownOn
            ? UpdateLockdown(cancellationToken)
            : Unblock(cancellationToken);
    }

    // This app's block, if one is up; another app's stays, as that app holds the tunnel.
    private async Task Unblock(CancellationToken cancellationToken)
    {
        using var scope = await _lock.LockAsync(cancellationToken).Vhc();
        await killSwitch.Unblock(anyApp: false, cancellationToken).Vhc();
        _isBlocking = await killSwitch.IsBlocking(cancellationToken).Vhc();
    }

    // The block with no session where it is kept while disconnected: up by the settings' split, the tunnel's
    // adapter by its name, though none is up; else this app's lifted. While a session runs, or a connect starts
    // one, the block is the service's, and the disconnect comes back here. Another app's connect replaces it, and
    // that app's disconnect lifts it; this app puts it back at its next start or settings change, not at once.
    private async Task UpdateLockdown(CancellationToken cancellationToken)
    {
        using var scope = await _lock.LockAsync(cancellationToken).Vhc();
        if (app.IsSessionless) {
            if (IsLockdownOn) {
                var splitTunneling = app.UserSettings.SplitTunneling.ToEffective(app);
                await killSwitch.Block(VpnKillSwitchRules.CreateBySettings(app.Config.AdapterName,
                    app.GetIncludeIpRangesByDevice(splitTunneling), splitTunneling.UseLocalNetwork),
                    cancellationToken).Vhc();
            }
            else {
                await killSwitch.Unblock(anyApp: false, cancellationToken).Vhc();
            }
        }

        _isBlocking = await killSwitch.IsBlocking(cancellationToken).Vhc();
    }

    private async Task Read(CancellationToken cancellationToken)
    {
        using var scope = await _lock.LockAsync(cancellationToken).Vhc();
        _isBlocking = await killSwitch.IsBlocking(cancellationToken).Vhc();
    }
}

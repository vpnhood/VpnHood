using System.CommandLine;
using VpnHood.AppLib.Api;
using VpnHood.AppUi.Hosting.Desktop.Daemon.Platform;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Desktop.Daemon.Commands;

// The kill switch (UserSettings.KillSwitch): from a connect until a person disconnects, nothing leaves outside
// the tunnel, through an error stop, a crash and a reboot; "lockdown" keeps the block after a disconnect too
// (UserSettings.KillSwitchLockdown). A box with no window turns them on and off here; bare, each says which.
// "off" is also a person's way out where the service does not run: the install's script lifts the block, with
// the switch left as it is for the service's next start, so a person is never stuck behind the block.
internal static class KillSwitchCommand
{
    public static Command Create(IDesktopPlatform platform)
    {
        var valueArgument = new Argument<string?>("on|off") {
            Description = "Turn it on or off. None says which it is.",
            Arity = ArgumentArity.ZeroOrOne
        };
        valueArgument.AcceptOnlyFromAmong("on", "off");

        var lockdownArgument = new Argument<string?>("on|off") {
            Description = "Turn it on or off. None says which it is.",
            Arity = ArgumentArity.ZeroOrOne
        };
        lockdownArgument.AcceptOnlyFromAmong("on", "off");

        var lockdownCommand = new Command("lockdown",
            "Block the internet while disconnected too, until this or the kill switch is turned off.") {
            lockdownArgument
        };
        lockdownCommand.SetAction((parseResult, cancellationToken) => DaemonSession.Run(platform,
            (api, token) => RunLockdown(api, parseResult.GetValue(lockdownArgument), token), cancellationToken));

        var command = new Command("kill-switch",
            "Let nothing out but the VPN tunnel from a connect until a disconnect, through a crash and a reboot.") {
            valueArgument,
            lockdownCommand
        };

        command.SetAction((parseResult, cancellationToken) =>
            Run(platform, parseResult.GetValue(valueArgument), cancellationToken));

        return command;
    }

    private static async Task<int> Run(IDesktopPlatform platform, string? value, CancellationToken cancellationToken)
    {
        if (value == "off" && platform.Paths.KillSwitchOffScriptPath is { } scriptPath &&
            !await platform.Instance.IsRunning(cancellationToken).Vhc()) {
            try {
                return await platform.RunKillSwitchOffScript(scriptPath, cancellationToken).Vhc();
            }
            catch (Exception ex) when (ex is not OperationCanceledException) {
                await Console.Error.WriteLineAsync(ex.Message).Vhc();
                return 1;
            }
        }

        return await DaemonSession.Run(platform, (api, token) => Run(api, value, token), cancellationToken).Vhc();
    }

    private static async Task<int> Run(VpnHoodApi api, string? value, CancellationToken cancellationToken)
    {
        var info = await api.App.GetInfo(cancellationToken).Vhc();
        var userSettings = info.UserSettings;
        if (value != null) {
            // the whole settings object goes back, as the UI saves it
            userSettings.KillSwitch = value == "on";
            await api.App.SetUserSettings(userSettings, cancellationToken).Vhc();
        }

        await CliPrinter.Line($"Kill switch: {(userSettings.KillSwitch ? "on" : "off")}", cancellationToken).Vhc();
        return 0;
    }

    private static async Task<int> RunLockdown(VpnHoodApi api, string? value, CancellationToken cancellationToken)
    {
        var info = await api.App.GetInfo(cancellationToken).Vhc();
        var userSettings = info.UserSettings;
        if (value != null) {
            userSettings.KillSwitchLockdown = value == "on";
            await api.App.SetUserSettings(userSettings, cancellationToken).Vhc();
        }

        await CliPrinter.Line($"Kill switch lockdown: {(userSettings.KillSwitchLockdown ? "on" : "off")}",
            cancellationToken).Vhc();
        return 0;
    }
}

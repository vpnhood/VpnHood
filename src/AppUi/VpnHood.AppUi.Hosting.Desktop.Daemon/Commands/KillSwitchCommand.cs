using System.CommandLine;
using VpnHood.AppLib.Api;
using VpnHood.AppLib.Api.Settings;
using VpnHood.AppUi.Hosting.Desktop.Daemon.Platform;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Desktop.Daemon.Commands;

// The kill switch's level (UserSettings.KillSwitch): "until-disconnect" lets nothing out but the tunnel from a
// connect until a person disconnects, through an error stop, a crash and a reboot; "lockdown" keeps the block after
// a disconnect too. A box with no window sets it here; bare, it says which.
// "off" is also a person's way out where the service does not run: the install's script lifts the block, with
// the level left as it is for the service's next start, so a person is never stuck behind the block.
internal static class KillSwitchCommand
{
    private static readonly (string Name, KillSwitchMode Mode)[] Levels = [
        ("off", KillSwitchMode.Off),
        ("until-disconnect", KillSwitchMode.UntilDisconnect),
        ("lockdown", KillSwitchMode.Lockdown)
    ];

    public static Command Create(IDesktopPlatform platform)
    {
        var names = Levels.Select(x => x.Name).ToArray();
        var valueArgument = new Argument<string?>(string.Join('|', names)) {
            Description = "The level to set. None says which it is.",
            Arity = ArgumentArity.ZeroOrOne
        };
        valueArgument.AcceptOnlyFromAmong(names);

        var command = new Command("kill-switch",
            "Let nothing out but the VPN tunnel: from a connect until a disconnect, through a crash and a reboot, " +
            "or with lockdown after a disconnect too.") {
            valueArgument
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
                await CliPrinter.Error(ex.Message).Vhc();
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
            userSettings.KillSwitch = Levels.Single(x => x.Name == value).Mode;
            await api.App.SetUserSettings(userSettings, cancellationToken).Vhc();
        }

        var name = Levels.Single(x => x.Mode == userSettings.KillSwitch).Name;
        await CliPrinter.Line($"Kill switch: {name}", cancellationToken).Vhc();
        return 0;
    }
}

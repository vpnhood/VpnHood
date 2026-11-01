using System.CommandLine;
using VpnHood.AppLib.Api;
using VpnHood.AppUi.Hosting.Desktop.Daemon.Platform;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Desktop.Daemon.Commands;

// The kill switch (UserSettings.KillSwitch): from a connect until a person disconnects, nothing leaves outside
// the tunnel, through an error stop, a crash and a reboot. A box with no window turns it on and off here; bare,
// it says which.
internal static class KillSwitchCommand
{
    public static Command Create(IDesktopPlatform platform)
    {
        var valueArgument = new Argument<string?>("on|off") {
            Description = "Turn it on or off. None says which it is.",
            Arity = ArgumentArity.ZeroOrOne
        };
        valueArgument.AcceptOnlyFromAmong("on", "off");

        var command = new Command("kill-switch",
            "Let nothing out but the VPN tunnel from a connect until a disconnect, through a crash and a reboot.") {
            valueArgument
        };

        command.SetAction((parseResult, cancellationToken) => DaemonSession.Run(platform,
            (api, token) => Run(api, parseResult.GetValue(valueArgument), token), cancellationToken));

        return command;
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
}

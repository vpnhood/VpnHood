using System.CommandLine;
using VpnHood.AppLib.Api;
using VpnHood.AppUi.Hosting.Desktop.Daemon.Platform;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Desktop.Daemon.Commands;

// Always on (UserSettings.AlwaysOn): the service connects by itself when it starts, at boot before anyone
// signs in, and again after a failure. A box with no window turns it on and off here; bare, it says which.
internal static class AlwaysOnCommand
{
    public static Command Create(IDesktopPlatform platform)
    {
        var valueArgument = new Argument<string?>("on|off") {
            Description = "Turn it on or off. None says which it is.",
            Arity = ArgumentArity.ZeroOrOne
        };
        valueArgument.AcceptOnlyFromAmong("on", "off");

        var command = new Command("always-on",
            "Connect when the service starts, before anyone signs in, and again after a failure.") { valueArgument };

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
            userSettings.AlwaysOn = value == "on";
            await api.App.SetUserSettings(userSettings, cancellationToken).Vhc();
        }

        await CliPrinter.Line($"Always on: {(userSettings.AlwaysOn ? "on" : "off")}", cancellationToken).Vhc();
        return 0;
    }
}

using System.CommandLine;
using VpnHood.AppLib.Api;
using VpnHood.AppLib.Api.App;
using VpnHood.AppUi.Hosting.Cli.Internal;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Cli.Commands;

// What the app is doing, the way the window's home screen says it. The exit code carries the same
// answer for a script that would rather branch than parse: 0 connected, 3 not.
//
// --json prints the app's own AppState, untouched. Everything the UI reads is in it, so a script
// that outgrows these eight lines does not outgrow the command.
internal static class StatusCommand
{
    private const int NotConnectedExitCode = 3;

    public static Command Create(CliPlatform platform)
    {
        var jsonOption = new Option<bool>("--json") {
            Description = "Print the app's full state as JSON."
        };
        var watchOption = new Option<bool>("--watch", "-w") {
            Description = "Keep printing until interrupted."
        };

        var command = new Command("status", "Show the connection state.") { jsonOption, watchOption };

        command.SetAction((parseResult, cancellationToken) => DaemonSession.Run(platform,
            (api, token) => Run(api, parseResult.GetValue(jsonOption), parseResult.GetValue(watchOption), token),
            cancellationToken));

        return command;
    }

    private static async Task<int> Run(VpnHoodApi api, bool json, bool watch, CancellationToken cancellationToken)
    {
        while (true) {
            var state = await api.App.GetState(cancellationToken).Vhc();

            if (json)
                await CliPrinter.Json(state, cancellationToken).Vhc();
            else
                await CliPrinter.Fields(Describe(state), cancellationToken).Vhc();

            if (!watch)
                return state.ConnectionState is AppConnectionState.Connected or AppConnectionState.Unstable
                    ? 0
                    : NotConnectedExitCode;

            await CliPrinter.Line(string.Empty, cancellationToken).Vhc();
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).Vhc();
        }
    }

    private static IReadOnlyList<(string, string)> Describe(AppState state)
    {
        var fields = new List<(string, string)> {
            ("State", state.ConnectionState.ToString()),
            ("Profile", state.ClientProfile?.ClientProfileName ?? "-"),
            ("Location", state.ServerLocationInfo?.ServerLocation ?? "-"),
            ("Protocol", state.ChannelProtocol.ToString())
        };

        var sessionStatus = state.SessionStatus;
        if (sessionStatus != null) {
            fields.Add(("Received", CliPrinter.Bytes(sessionStatus.SessionTraffic.Received)));
            fields.Add(("Sent", CliPrinter.Bytes(sessionStatus.SessionTraffic.Sent)));
            fields.Add(("Speed down", $"{CliPrinter.Bytes(sessionStatus.Speed.Received)}/s"));
            fields.Add(("Speed up", $"{CliPrinter.Bytes(sessionStatus.Speed.Sent)}/s"));
        }

        if (state.ClientCountryInfo != null)
            fields.Add(("Country", state.ClientCountryInfo.EnglishName));

        if (state.LastError != null)
            fields.Add(("Last error", state.LastError.Message));

        return fields;
    }
}

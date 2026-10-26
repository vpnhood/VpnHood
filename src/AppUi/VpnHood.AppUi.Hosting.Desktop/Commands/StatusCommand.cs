using System.CommandLine;
using VpnHood.AppLib.Api;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.Updaters;
using VpnHood.AppUi.Hosting.Desktop.Platform;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Desktop.Commands;

// What the app is doing, the way the window's home screen says it. The exit code carries the same
// answer for a script that would rather branch than parse: 0 connected, 3 not.
//
// --json prints the app's own AppState, untouched. Everything the UI reads is in it, so a script
// that outgrows these few lines does not outgrow the command.
internal static class StatusCommand
{
    private const int NotConnectedExitCode = 3;

    public static Command Create(IDesktopPlatform platform)
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
            else {
                // asked only where the update's line depends on it: whether it comes by itself
                var isAutomaticUpdateOn = state.UpdaterStatus?.UpdateState?.Stage is AppUpdateStage.Available
                                              or AppUpdateStage.Downloaded &&
                                          await api.App.GetAutomaticUpdate(cancellationToken).Vhc();
                await CliPrinter.Fields(Describe(state, isAutomaticUpdateOn), cancellationToken).Vhc();
            }

            if (!watch)
                return state.ConnectionState is AppConnectionState.Connected or AppConnectionState.Unstable
                    ? 0
                    : NotConnectedExitCode;

            await CliPrinter.Line(string.Empty, cancellationToken).Vhc();
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).Vhc();
        }
    }

    private static IReadOnlyList<(string, string)> Describe(AppState state, bool isAutomaticUpdateOn)
    {
        var fields = new List<(string, string)> {
            ("State", state.ConnectionState.ToString()),
            ("Profile", state.VpnProfile?.VpnProfileName ?? "-"),
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

        if (state.AlwaysOnState != null)
            fields.Add(("Always on", Describe(state.AlwaysOnState)));

        if (Describe(state.UpdaterStatus, isAutomaticUpdateOn) is { } update)
            fields.Add(("Update", update));

        return fields;
    }

    // The newer version and how far its update has come, as the window's notice shows it; null with none. The install
    // itself is the state's Updating too.
    internal static string? Describe(AppUpdaterStatus? updaterStatus, bool isAutomaticUpdateOn)
    {
        // an app whose updater has no part in the notice still knows the feed's newer version
        if (updaterStatus?.UpdateState is not { } update)
            return updaterStatus is
                { VersionStatus: VersionStatus.Old or VersionStatus.Deprecated, PublishInfo: { } info }
                ? $"{info.Version} available"
                : null;

        return update.Stage switch {
            AppUpdateStage.Available => isAutomaticUpdateOn
                ? $"{update.Version} available"
                : $"{update.Version} available; automatic updates are off",
            AppUpdateStage.Downloading => update.TotalBytes is { } total and > 0
                ? $"downloading {update.Version}: {update.DownloadedBytes * 100 / total}%"
                : $"downloading {update.Version}: {CliPrinter.Bytes(update.DownloadedBytes)}",
            AppUpdateStage.Downloaded => isAutomaticUpdateOn
                ? $"{update.Version} downloaded, installs while the VPN is disconnected"
                : $"{update.Version} downloaded; automatic updates are off",
            AppUpdateStage.Installing => $"installing {update.Version}",
            AppUpdateStage.Failed => $"updating to {update.Version} failed: {update.ErrorMessage}",
            _ => null
        };
    }

    // why it waits and until when, or why it stopped; a person's connect starts it again either way
    private static string Describe(AlwaysOnState alwaysOnState)
    {
        return alwaysOnState.Status switch {
            AlwaysOnStatus.Waiting =>
                $"waiting until {alwaysOnState.RetryTime?.ToLocalTime():T}: {alwaysOnState.Error?.Message}",
            AlwaysOnStatus.Stopped when alwaysOnState.Error is null => "stopped until the next connect",
            AlwaysOnStatus.Stopped => $"stopped until the next connect: {alwaysOnState.Error.Message}",
            _ => alwaysOnState.Status.ToString().ToLowerInvariant()
        };
    }
}

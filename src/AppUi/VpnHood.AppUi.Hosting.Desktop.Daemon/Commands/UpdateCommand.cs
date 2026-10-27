using System.CommandLine;
using VpnHood.AppLib.Api;
using VpnHood.AppLib.Api.Updaters;
using VpnHood.AppUi.Hosting.Desktop.Daemon.Platform;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppUi.Hosting.Desktop.Daemon.Commands;

// The window's "Check for update", then its Update and Install, from a terminal: a machine with no window has no other
// way to update now. It checks the feed itself, so a release the service's daily check has not seen yet counts, and
// installs whatever automatic updates are set to, as the person asked for it. The install disconnects the VPN, as the
// notice warns.
internal static class UpdateCommand
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    public static Command Create(IDesktopPlatform platform)
    {
        var command = new Command("update",
            "Install the newer version now, downloading it first. The VPN disconnects while it installs.");

        command.SetAction((_, cancellationToken) => DaemonSession.Run(platform, Run, cancellationToken));
        return command;
    }

    private static async Task<int> Run(VpnHoodApi api, CancellationToken cancellationToken)
    {
        var info = await api.App.GetInfo(cancellationToken).Vhc();
        var appVersion = info.Features.Version;
        var checkResult = await api.App.VersionCheck(cancellationToken).Vhc();
        if (checkResult == VersionCheckResult.Failed) {
            await Console.Error.WriteLineAsync("Could not check for an update; see the service's log.").Vhc();
            return 1;
        }

        var status = (await api.App.GetState(cancellationToken).Vhc()).UpdaterStatus;
        if (checkResult == VersionCheckResult.Latest || status is null) {
            await CliPrinter.Line($"{appVersion} is the latest version.", cancellationToken).Vhc();
            return 0;
        }

        // the feed's newer version, which this install does not take itself
        if (status.UpdateState is not { } update) {
            await Console.Error.WriteLineAsync(
                $"This install does not update itself. Get {status.PublishInfo?.Version} at: " +
                $"{status.PublishInfo?.InstallationPageUrl}").Vhc();
            return 1;
        }

        var version = update.Version;
        if (update.Stage is not AppUpdateStage.Installing) {
            if (await Download(api, update, cancellationToken).Vhc() is { } downloadError) {
                await Console.Error.WriteLineAsync($"Updating to {version} failed: {downloadError}").Vhc();
                return 1;
            }

            await api.App.CompleteUpdate(cancellationToken).Vhc();
        }

        // Done once the installer has it: Windows' setup closes whatever runs from the install's folder, this command
        // included, so none waits for the new version; status says how the install goes.
        await CliPrinter.Line(
            $"Installing {version}: the VPN disconnects, and the service starts again on it. " +
            "'status' says when it is back.", cancellationToken).Vhc();
        return 0;
    }

    // Until the package is down, its progress a line each tenth; null when it is, else why not
    private static async Task<string?> Download(VpnHoodApi api, AppUpdateState update,
        CancellationToken cancellationToken)
    {
        if (update.Stage == AppUpdateStage.Downloaded)
            return null;

        if (update.Stage != AppUpdateStage.Downloading)
            await api.App.StartUpdate(cancellationToken).Vhc();

        var printedTenth = -1L;
        while (true) {
            var state = (await api.App.GetState(cancellationToken).Vhc()).UpdaterStatus?.UpdateState;
            switch (state?.Stage) {
                case AppUpdateStage.Downloaded:
                    return null;

                case AppUpdateStage.Failed:
                    return state.ErrorMessage;

                case AppUpdateStage.Downloading:
                    var tenth = state.TotalBytes is { } total and > 0 ? state.DownloadedBytes * 10 / total : 0;
                    if (tenth > printedTenth) {
                        printedTenth = tenth;
                        await CliPrinter.Line($"Downloading {state.Version}: {tenth * 10}%", cancellationToken).Vhc();
                    }

                    break;

                case null:
                    return "the newer version is no longer offered.";
            }

            await Task.Delay(PollInterval, cancellationToken).Vhc();
        }
    }
}

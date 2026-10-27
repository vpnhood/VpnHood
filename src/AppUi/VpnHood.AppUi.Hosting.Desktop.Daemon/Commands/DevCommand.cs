using System.CommandLine;
using Microsoft.Extensions.Logging;
using VpnHood.AppUi.Hosting.Desktop.Daemon.Platform;
using VpnHood.AppUi.Hosting.Desktop.Window;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Desktop.Daemon.Commands;

// The daemon and the window in this one process, for a debugger: a breakpoint in the app, its tunnel
// or its UI is reached from one run, with no service installed. The window reaches the app over
// loopback as it reaches the service, so what runs is the real path but for the process boundary.
// The app keeps its storage in the person's own folder and runs as whoever started it.
internal static class DevCommand
{
    public static Command Create(IDesktopPlatform platform, DesktopInitParams initParams, MainThreadQueue mainThread)
    {
        // the window's own --access-key, for a debugger of the import
        var accessKeyOption = AccessKeyInput.CreateOption();
        var command = new Command("dev", "Run the service and the window in this one process, for a debugger.") {
            Hidden = true,
            Options = { accessKeyOption }
        };

        command.SetAction((parseResult, cancellationToken) => Run(platform, initParams, mainThread,
            parseResult.GetValue(accessKeyOption), cancellationToken));

        return command;
    }

    private static async Task<int> Run(IDesktopPlatform platform, DesktopInitParams initParams, MainThreadQueue mainThread,
        string? accessKeyInput, CancellationToken cancellationToken)
    {
        // the platform's console from the first line, as the daemon has it
        VhLogger.AddProvider(platform.CreateConsoleLoggerProvider());

        try {
            var accessKey = accessKeyInput == null ? null : AccessKeyInput.Read(accessKeyInput);
            await using var daemonHost = platform.DaemonHostFactory.CreateDev(platform.Paths.DevStoragePath);
            await InProcessDesktopHost.RunWindow(platform.Paths, platform.PeerCheck, initParams, mainThread,
                useTray: true, shell: null, startHidden: false, accessKey, isQuickLaunch: false, cancellationToken).Vhc();
            return 0;
        }
        catch (OperationCanceledException) {
            return 130;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "{InstanceName} could not run.", platform.Paths.InstanceName);
            await Console.Error.WriteLineAsync(ex.Message).Vhc();
            return 1;
        }
    }
}

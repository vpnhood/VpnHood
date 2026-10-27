using System.CommandLine;
using Microsoft.Extensions.Logging;
using VpnHood.AppUi.Hosting.Desktop.Abstractions;
using VpnHood.AppUi.Hosting.Desktop.Daemon.Channel;
using VpnHood.AppUi.Hosting.Desktop.Daemon.Platform;
using VpnHood.AppUi.Hosting.Desktop.Exceptions;
using VpnHood.AppUi.Hosting.Desktop.Window;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Desktop.Daemon.Commands;

// The window, run by whoever is logged in. It holds no VpnHoodApp and needs no privilege: it reads
// and drives the daemon's app over loopback, which is the same API a paired phone's page uses. That
// is what lets it run as a normal user - so a link opens in that person's browser, and so a Wayland
// session is not asked to show a root window, which it would refuse.
//
// This is what the desktop entry starts. A person who clicks an icon has nowhere to read an error,
// so a stopped instance is started here rather than reported: on Linux that is systemctl asking
// the session's polkit agent, on Windows the service control manager, which lets any signed-in
// person start the service. Waiting for it to come up is not done here - DaemonChannelConnector.Open
// waits for any caller. What stops the window - a person who may not use the app, a service that
// could not be started or reached - the UI says in one message in its place (IDesktopUi.RunMessage).
internal static class UiCommand
{
    public static Command Create(IDesktopPlatform platform, DesktopInitParams initParams, MainThreadQueue mainThread)
    {
        var command = new Command("ui", "Open the app window. This is what the desktop entry starts.");

        // The entry an installer runs at sign-in starts the UI in its tray, with the window closed; a
        // UI with no tray to show ends at once. For that entry, not for a person, so help leaves it out.
        var trayOption = new Option<bool>("--tray") {
            Description = "Start in the tray, with the window closed.",
            Hidden = true
        };
        command.Options.Add(trayOption);

        // An access key's file opened with the app: the window imports the key.
        var accessKeyOption = AccessKeyInput.CreateOption();
        command.Options.Add(accessKeyOption);

        command.SetAction((parseResult, cancellationToken) => Run(platform, initParams, mainThread,
            startHidden: parseResult.GetValue(trayOption), parseResult.GetValue(accessKeyOption), cancellationToken));

        return command;
    }

    private static async Task<int> Run(IDesktopPlatform platform, DesktopInitParams initParams, MainThreadQueue mainThread,
        bool startHidden, string? accessKeyInput, CancellationToken cancellationToken)
    {
        // before anything logs: the window has no console, so its warnings and errors go to the
        // platform's own log
        VhLogger.AddProvider(platform.CreateSystemLogLoggerProvider());

        try {
            bool isServiceNewer;

            // read before the window is looked for, which a second launch hands it to: a file that
            // cannot be read is this launch's failure, said in its own message
            var accessKey = accessKeyInput == null ? null : await DesktopUiWindow.TryReadAccessKey(platform.Paths,
                initParams, mainThread, accessKeyInput, cancellationToken).Vhc();
            if (accessKeyInput != null && accessKey == null)
                return 1;

            // One window per person and session: a second launch brings the open one forward, after
            // handing it the key it was given, and that is all it does.
            await using (var uiInstance = DesktopUiInstance.TryClaim(platform.Paths, platform.DesktopSession,
                initParams.Ui, accessKey)) {
                if (uiInstance == null)
                    return 0;

                await using var connection = await TryReach(platform, initParams, mainThread, startHidden,
                    cancellationToken).Vhc();
                if (connection == null)
                    return 1;

                isServiceNewer = await DesktopUiWindow.RunWindow(platform.Paths, initParams, mainThread, connection,
                    startHidden, accessKey, cancellationToken).Vhc();
            }

            // out of this window's claim, which the new one takes
            if (isServiceNewer) {
                VhLogger.Instance.LogInformation("The service is a newer version; starting the window again on it.");
                platform.StartUiAgain(startHidden);
            }
            return 0;
        }
        catch (OperationCanceledException) {
            return 130;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "The window ended with an error.");
            await Console.Error.WriteLineAsync(ex.Message).Vhc();
            return 1;
        }
    }

    // The app's API; or null once the person has been told why it cannot be had, in the one message
    // the UI shows in its place. The tray started at sign-in tells nobody: a standard user's exits
    // silently (desktop plan §3).
    private static async Task<DaemonConnection?> TryReach(IDesktopPlatform platform, DesktopInitParams initParams,
        MainThreadQueue mainThread, bool startHidden, CancellationToken cancellationToken)
    {
        try {
            return await Reach(platform, startHidden, cancellationToken).Vhc();
        }
        catch (DaemonRefusedException ex) {
            if (!startHidden)
                await DesktopUiWindow.RunMessage(platform.Paths, initParams, mainThread,
                    DesktopUiMessageKind.AdministratorsOnly, ex.Message, cancellationToken).Vhc();
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception ex) {
            // the log has it whether or not anyone was told, and "service log" shows it while the
            // service gives none
            VhLogger.Instance.LogWarning(ex, "The window could not reach the service.");
            if (!startHidden)
                await DesktopUiWindow.RunMessage(platform.Paths, initParams, mainThread, DesktopUiMessageKind.Failure,
                    ex.Message, cancellationToken).Vhc();
        }

        return null;
    }

    // The window's own check of the person comes first, so a standard user meets no prompt: neither
    // the service's start nor its registration, which asks UAC. The tray started at sign-in starts
    // a stopped service but registers no missing one: nobody opened anything, to be asked. Throws,
    // saying why, when the app cannot be had.
    private static async Task<DaemonConnection> Reach(IDesktopPlatform platform, bool startHidden,
        CancellationToken cancellationToken)
    {
        if (!platform.IsAdministrator())
            throw new DaemonRefusedException(platform.AdministratorsOnlyMessage);

        if (!await platform.Instance.IsRunning(cancellationToken).Vhc())
            await platform.Instance.Start(mayRegister: !startHidden, cancellationToken).Vhc();

        return await DaemonChannelConnector.Open(platform, cancellationToken).Vhc();
    }
}

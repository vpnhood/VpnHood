using System.CommandLine;
using Microsoft.Extensions.Logging;
using VpnHood.AppUi.Hosting.Abstractions;
using VpnHood.AppUi.Hosting.Desktop.Abstractions;
using VpnHood.AppUi.Hosting.Desktop.Exceptions;
using VpnHood.Net.Toolkit.Assets;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Desktop.Commands;

// The window, run by whoever is logged in. It holds no VpnHoodApp and needs no privilege: it reads
// and drives the daemon's app over loopback, which is the same API a paired phone's page uses. That
// is what lets it run as a normal user - so a link opens in that person's browser, and so a Wayland
// session is not asked to show a root window, which it would refuse.
//
// This is what the desktop entry starts. A person who clicks an icon has nowhere to read an error,
// so a stopped instance is started here rather than reported: on Linux that is systemctl asking
// the session's polkit agent, on Windows the service control manager, which lets any signed-in
// person start the service. Waiting for it to come up is not done here - DaemonConnection.Open
// waits for any caller. What stops the window - a person who may not use the app, a service that
// could not be started or reached - the UI says in one message in its place (IDesktopUi.RunMessage).
internal static class UiCommand
{
    public static Command Create(DesktopPlatform platform, DesktopInitParams initParams, MainThreadQueue mainThread)
    {
        var command = new Command("ui", "Open the app window. This is what the desktop entry starts.");

        // Where a tray keeps the UI, an entry that starts at logon starts it there, with the window
        // closed. For the entry an installer writes, not for a person, so help leaves it out.
        var trayOption = new Option<bool>("--tray") {
            Description = "Start in the tray, with the window closed.",
            Hidden = true
        };

        if (platform.CreateTray != null)
            command.Options.Add(trayOption);

        command.SetAction((parseResult, cancellationToken) => Run(platform, initParams, mainThread,
            startHidden: platform.CreateTray != null && parseResult.GetValue(trayOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> Run(DesktopPlatform platform, DesktopInitParams initParams, MainThreadQueue mainThread,
        bool startHidden, CancellationToken cancellationToken)
    {
        // before anything logs: the window has no console, so its warnings and errors go to the
        // platform's own log
        VhLogger.AddProvider(platform.CreateSystemLogLoggerProvider());

        try {
            // One window per person: a second launch brings the open one forward, and that is all it does.
            await using var uiInstance = await DesktopUiInstance.TryClaim(platform.Paths.InstanceName, initParams.Ui,
                cancellationToken).Vhc();
            if (uiInstance == null)
                return 0;

            await using var connection = await TryReach(platform, initParams, mainThread, startHidden,
                cancellationToken).Vhc();
            if (connection == null)
                return 1;

            await RunWindow(platform, initParams, mainThread, connection, startHidden, cancellationToken).Vhc();
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
    private static async Task<DaemonConnection?> TryReach(DesktopPlatform platform, DesktopInitParams initParams,
        MainThreadQueue mainThread, bool startHidden, CancellationToken cancellationToken)
    {
        try {
            return await Reach(platform, startHidden, cancellationToken).Vhc();
        }
        catch (DaemonRefusedException ex) {
            if (!startHidden)
                await RunMessage(platform, initParams, mainThread, DesktopUiMessageKind.AdministratorsOnly, ex.Message,
                    cancellationToken).Vhc();
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception ex) {
            // the log has it whether or not anyone was told, and "service log" shows it while the
            // service gives none
            VhLogger.Instance.LogWarning(ex, "The window could not reach the service.");
            if (!startHidden)
                await RunMessage(platform, initParams, mainThread, DesktopUiMessageKind.Failure, ex.Message,
                    cancellationToken).Vhc();
        }

        return null;
    }

    // The UI's one message in the window's place, on the host's main thread as the window would run,
    // until the person closes it.
    private static Task RunMessage(DesktopPlatform platform, DesktopInitParams initParams, MainThreadQueue mainThread,
        DesktopUiMessageKind kind, string text, CancellationToken cancellationToken)
    {
        return mainThread.Run(() => initParams.Ui.RunMessage(new DesktopUiMessageParams {
            Kind = kind,
            AppName = initParams.AppName,
            Text = text,
            UiAssetProvider = CreateUiAssets(platform, initParams)
        }, cancellationToken));
    }

    // The window's own check of the person comes first, so a standard user meets no prompt: neither
    // the service's start nor its registration, which asks UAC. The tray started at sign-in starts
    // a stopped service but registers no missing one: nobody opened anything, to be asked. Throws,
    // saying why, when the app cannot be had.
    private static async Task<DaemonConnection> Reach(DesktopPlatform platform, bool startHidden,
        CancellationToken cancellationToken)
    {
        if (!platform.IsAdministrator())
            throw new DaemonRefusedException(platform.AdministratorsOnlyMessage);

        if (!await platform.Instance.IsRunning(cancellationToken).Vhc())
            await platform.Instance.Start(mayRegister: !startHidden, cancellationToken).Vhc();

        return await DaemonConnection.Open(platform, cancellationToken).Vhc();
    }

    // The UI's own copy of the content, under this user's cache: the daemon extracted its own under
    // its storage, which a session may not write, and each provider owns its folder.
    private static IAssetProvider CreateUiAssets(DesktopPlatform platform, DesktopInitParams initParams)
    {
        var packagedAssetProvider = new FolderAssetProvider(AppContext.BaseDirectory);
        return new ZipAssetProvider(new Asset(packagedAssetProvider, initParams.UiZipAssetPath),
            platform.Paths.UiContentCachePath);
    }

    // The window over a connection to the app, until it is gone: the service's app, or the one this
    // process holds itself (DevCommand).
    internal static async Task RunWindow(DesktopPlatform platform, DesktopInitParams initParams, MainThreadQueue mainThread,
        DaemonConnection connection, bool startHidden, CancellationToken cancellationToken)
    {
        var uiAssets = CreateUiAssets(platform, initParams);

        // The UI's run ends with the command - a signal, a logout - or with the tray's Exit.
        using var uiCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var tray = platform.CreateTray?.Invoke(new DesktopTrayParams {
            Api = connection.Api,
            UiAssets = uiAssets,
            ShowWindow = initParams.Ui.BringToFront,
            Exit = uiCancellation.Cancel
        });

        // on the host's main thread, which a window needs; this waits until it is gone
        var uiParams = new DesktopUiParams {
            Api = connection.Api,
            UiAssetProvider = uiAssets,
            ApiUrlProvider = connection.ApiUrlProvider,
            UiDataPath = platform.Paths.UiDataPath,
            ExitOnClose = platform.CreateTray == null,
            StartHidden = startHidden
        };
        await mainThread.Run(() => initParams.Ui.Run(uiParams, uiCancellation.Token)).Vhc();
    }
}

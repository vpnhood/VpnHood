using System.CommandLine;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Api.App;
using VpnHood.AppUi.Hosting.Abstractions;
using VpnHood.AppUi.Hosting.Cli.Internal;
using VpnHood.Net.Toolkit.Assets;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Cli.Commands;

// The window, run by whoever is logged in. It holds no VpnHoodApp and needs no privilege: it reads
// and drives the daemon's app over loopback, which is the same API a paired phone's page uses. That
// is what lets it run as a normal user - so a link opens in that person's browser, and so a Wayland
// session is not asked to show a root window, which it would refuse.
//
// This is what the desktop entry starts. A person who clicks an icon has nowhere to read an error,
// so a stopped instance is started here rather than reported: on Linux that is systemctl asking
// the session's polkit agent, on Windows the service control manager, which lets any signed-in
// person start the service. Waiting for it to come up is not done here - DaemonConnection.Open
// waits for any caller.
internal static class UiCommand
{
    public static Command Create(CliPlatform platform, CliHeadParams head, MainThreadQueue mainThread)
    {
        var command = new Command("ui", "Open the app window. This is what the desktop entry starts.");

        // Where a tray keeps the UI, an entry that starts at logon starts it there, with the window
        // closed; an earlier release's /autoconnect also connects. For the entries an installer
        // writes, not for a person, so help leaves them out.
        var trayOption = new Option<bool>("--tray") {
            Description = "Start in the tray, with the window closed.",
            Hidden = true
        };
        var connectOption = new Option<bool>("--connect") {
            Description = "Connect as soon as the service is reached.",
            Hidden = true
        };

        if (platform.CreateTray != null) {
            command.Options.Add(trayOption);
            command.Options.Add(connectOption);
        }

        command.SetAction((parseResult, cancellationToken) => Run(platform, head, mainThread,
            startHidden: platform.CreateTray != null && parseResult.GetValue(trayOption),
            connect: platform.CreateTray != null && parseResult.GetValue(connectOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> Run(CliPlatform platform, CliHeadParams head, MainThreadQueue mainThread,
        bool startHidden, bool connect, CancellationToken cancellationToken)
    {
        try {
            // One window per person: a second launch brings the open one forward, and that is all it does.
            await using var uiInstance = await DesktopUiInstance.TryClaim(platform.Paths.InstanceName, head.Ui,
                cancellationToken).Vhc();
            if (uiInstance == null)
                return 0;

            if (!await platform.Instance.IsRunning(cancellationToken).Vhc() &&
                await platform.Instance.Start(cancellationToken).Vhc() != 0) {
                await Console.Error.WriteLineAsync(
                    $"Could not start {platform.Paths.InstanceName}.").Vhc();
                return 1;
            }

            using var connection = await DaemonConnection.Open(platform, cancellationToken).Vhc();
            await RunWindow(platform, head, mainThread, connection, startHidden, connect, cancellationToken).Vhc();
            return 0;
        }
        catch (OperationCanceledException) {
            return 130;
        }
        catch (Exception ex) {
            await Console.Error.WriteLineAsync(ex.Message).Vhc();
            return 1;
        }
    }

    // The window over a connection to the app, until it is gone: the service's app, or the one this
    // process holds itself (DevCommand).
    internal static async Task RunWindow(CliPlatform platform, CliHeadParams head, MainThreadQueue mainThread,
        DaemonConnection connection, bool startHidden, bool connect, CancellationToken cancellationToken)
    {
        if (connect)
            _ = Connect(connection, cancellationToken);

        // The UI's own copy of the content, under this user's cache: the daemon extracted its own
        // under its storage, which a session may not write, and each provider owns its folder.
        var packagedAssetProvider = new FolderAssetProvider(AppContext.BaseDirectory);
        var uiAssets = new ZipAssetProvider(
            new Asset(packagedAssetProvider, head.UiZipAssetPath), platform.Paths.UiContentCachePath);

        // The UI's run ends with the command - a signal, a logout - or with the tray's Exit.
        using var uiCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var tray = platform.CreateTray?.Invoke(new DesktopTrayParams {
            Api = connection.Api,
            UiAssets = uiAssets,
            ShowWindow = head.Ui.BringToFront,
            Exit = uiCancellation.Cancel
        });

        // on the host's main thread, which a window needs; this waits until it is gone
        var uiParams = new DesktopUiParams {
            Api = connection.Api,
            UiAssetProvider = uiAssets,
            WebUrl = connection.Url,
            UiDataPath = platform.Paths.UiDataPath,
            ExitOnClose = platform.CreateTray == null,
            StartHidden = startHidden
        };
        await mainThread.Run(() => head.Ui.Run(uiParams, uiCancellation.Token)).Vhc();
    }

    // Beside the window, which shows how it goes; a connect that fails is the service's to report.
    private static async Task Connect(DaemonConnection connection, CancellationToken cancellationToken)
    {
        try {
            await connection.Api.App.Connect(null, null, ConnectPlanId.Normal, cancellationToken).Vhc();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested) {
            VhLogger.Instance.LogWarning(ex, "Could not connect at the window's start.");
        }
    }
}

using Microsoft.Extensions.Logging;
using VpnHood.AppUi.Hosting.Desktop.Abstractions;
using VpnHood.AppUi.Hosting.Desktop.Platform;
using VpnHood.Net.Toolkit.Assets;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Desktop.Window;

// The window and the one message shown in its place, for both of the window's hosts: the service's
// window (UiCommand) and the app's own process (InProcessDesktopHost).
internal static class DesktopUiWindow
{
    // The key a launch was given, out of its file; or null once the person has been told why the file
    // could not be read, in the UI's one message - in the window's place, or beside the open one.
    internal static async Task<string?> TryReadAccessKey(IDesktopUiPaths paths, DesktopInitParams initParams,
        MainThreadQueue mainThread, string accessKeyInput, CancellationToken cancellationToken)
    {
        try {
            return AccessKeyInput.Read(accessKeyInput);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            VhLogger.Instance.LogError(ex, "Could not read the access key's file.");
            await RunMessage(paths, initParams, mainThread, DesktopUiMessageKind.Failure, ex.Message,
                cancellationToken).Vhc();
            return null;
        }
    }

    // The UI's one message in the window's place, on the host's main thread as the window would run,
    // until the person closes it.
    internal static Task RunMessage(IDesktopUiPaths paths, DesktopInitParams initParams, MainThreadQueue mainThread,
        DesktopUiMessageKind kind, string text, CancellationToken cancellationToken)
    {
        return mainThread.Run(() => initParams.Ui.RunMessage(new DesktopUiMessageParams {
            Kind = kind,
            AppName = initParams.AppName,
            Text = text,
            UiAssetProvider = CreateUiAssets(paths, initParams)
        }, cancellationToken));
    }

    // The UI over a connection to the app, until it ends: the service's app, or the one this process
    // holds itself (InProcessDesktopHost). It ends by itself - its window closed, its tray's Exit - or
    // with the command: a signal, a logout; or once the service answers as a newer version - an update
    // replaced it under the open window - which this returns true for. The UI imports the access key
    // the launch was given, if any, and connects for a Quick Launch.
    internal static async Task<bool> RunWindow(IDesktopUiPaths paths, DesktopInitParams initParams,
        MainThreadQueue mainThread, DaemonConnection connection, bool useTray, bool keepRunningWhileConnected,
        IDesktopShell? shell, bool startHidden, string? accessKey, bool isQuickLaunch, CancellationToken cancellationToken)
    {
        using var serviceNewer = new CancellationTokenSource();
        using var uiCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, serviceNewer.Token);
        void OnNewerServiceAnswered(object? sender, EventArgs e) => serviceNewer.Cancel();
        connection.NewerServiceAnswered += OnNewerServiceAnswered;

        // on the host's main thread, which a window needs; this waits until the UI ends
        var uiParams = new DesktopUiParams {
            Api = connection.Api,
            UiAssetProvider = CreateUiAssets(paths, initParams),
            ApiUrlProvider = connection.ApiUrlProvider,
            UiDataPath = paths.UiDataPath,
            StartHidden = startHidden,
            UseTray = useTray,
            KeepRunningWhileConnected = keepRunningWhileConnected,
            Shell = shell,
            AccessKey = accessKey,
            IsQuickLaunch = isQuickLaunch
        };
        try {
            await mainThread.Run(() => initParams.Ui.Run(uiParams, uiCancellation.Token)).Vhc();
        }
        finally {
            connection.NewerServiceAnswered -= OnNewerServiceAnswered;
        }

        return serviceNewer.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
    }

    // The UI's own copy of the content, under this user's cache: the daemon extracted its own under
    // its storage, which a session may not write, and each provider owns its folder.
    private static IAssetProvider CreateUiAssets(IDesktopUiPaths paths, DesktopInitParams initParams)
    {
        var packagedAssetProvider = new FolderAssetProvider(AppContext.BaseDirectory);
        return new ZipAssetProvider(new Asset(packagedAssetProvider, initParams.UiZipAssetPath),
            paths.UiContentCachePath);
    }
}

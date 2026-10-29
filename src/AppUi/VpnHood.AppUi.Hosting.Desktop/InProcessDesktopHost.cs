using System.CommandLine;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.App;
using VpnHood.AppUi.Hosting.Desktop.Abstractions;
using VpnHood.AppUi.Hosting.Desktop.Platform;
using VpnHood.AppUi.Hosting.Desktop.Window;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Desktop;

// The app and its window in one process, as a host's whole job: a desktop whose tunnel runs where the
// OS starts it - the Windows VPN plug-in's VPN host - needs no service, and its window no channel and
// no command line. One window per person and session, as under the service: a second launch brings
// it forward (DesktopUiInstance) and builds no app.
public static class InProcessDesktopHost
{
    // The whole run, on the calling thread, which is the host's main thread and the window's, as in
    // DesktopHost.Run. A window's process ends with its window: nothing cancels it from outside. The
    // arguments acted on are --access-key, which a vh: link passes (the package's scheme), and
    // --quick-launch, which the package's Quick Launch entry passes (vh-quick-launch:); the log keeps
    // them all.
    public static int Run(string[] args, DesktopInitParams initParams, IInProcessDesktopPlatform platform)
    {
        using var mainThread = new MainThreadQueue();
        var run = Task.Run(() => RunAsync(args, initParams, platform, mainThread, CancellationToken.None));
        mainThread.RunUntil(run);
        return run.GetAwaiter().GetResult();
    }

    private static async Task<int> RunAsync(string[] args, DesktopInitParams initParams,
        IInProcessDesktopPlatform platform, MainThreadQueue mainThread, CancellationToken cancellationToken)
    {
        try {
            // read before the window is looked for, which a second launch hands them to, as the service's
            // window does (UiCommand)
            var accessKeyOption = AccessKeyInput.CreateOption();
            var quickLaunchOption = CreateQuickLaunchOption();
            var parseResult = new RootCommand { accessKeyOption, quickLaunchOption }.Parse(args);
            var accessKeyInput = parseResult.GetValue(accessKeyOption);
            var isQuickLaunch = parseResult.GetValue(quickLaunchOption);
            var accessKey = accessKeyInput == null ? null : await DesktopUiWindow.TryReadAccessKey(platform.Paths,
                initParams, mainThread, accessKeyInput, cancellationToken).Vhc();
            if (accessKeyInput != null && accessKey == null)
                return 1;

            await using var uiInstance = DesktopUiInstance.TryClaim(platform.Paths, platform.DesktopSession,
                initParams.Ui, accessKey, isQuickLaunch);
            if (uiInstance == null)
                return 0;

            VhLogger.AddProvider(platform.CreateLoggerProvider());
            VhLogger.Instance.LogInformation("{InstanceName} started on .NET {Version}. Args: {Args}",
                platform.Paths.InstanceName, Environment.Version, string.Join(' ', args));

            try {
                await using var app = platform.CreateApp();
                await RunWindow(platform.Paths, platform.PeerCheck, initParams, mainThread, platform.UseTray,
                    platform.Shell, startHidden: false, accessKey, isQuickLaunch, cancellationToken).Vhc();
                return 0;
            }
            catch (Exception ex) {
                // in the window's place: a person who clicked an icon has nowhere else to read it
                VhLogger.Instance.LogError(ex, "{InstanceName} could not run.", platform.Paths.InstanceName);
                await DesktopUiWindow.RunMessage(platform.Paths, initParams, mainThread, DesktopUiMessageKind.Failure,
                    ex.Message, cancellationToken).Vhc();
                return 1;
            }
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "The window ended with an error.");
            return 1;
        }
    }

    // The window over the app this process holds, until the window ends: this host's, and a debugger's
    // beside the service's (DevCommand). Over the app's loopback API, as the service's window reaches it.
    // The app ends with the window, so a close while its VPN is up only hides the window
    // (DesktopUiParams.KeepRunningWhileConnected).
    internal static async Task RunWindow(IDesktopUiPaths paths, ILoopbackPeerCheck peerCheck,
        DesktopInitParams initParams, MainThreadQueue mainThread, bool useTray, IDesktopShell? shell, bool startHidden,
        string? accessKey, bool isQuickLaunch, CancellationToken cancellationToken)
    {
        var localWebHost = VpnHoodApp.Instance.LocalWebHost ??
                           throw new InvalidOperationException(
                               "The app has no local web host, so the window could not reach it.");
        await localWebHost.EnsureStarted(cancellationToken).Vhc();

        // No channel to ask: the app is here, and so is the process the window's calls must reach.
        await using var connection = DaemonConnection.CreateInProcess(localWebHost, peerCheck, paths.InstanceName);
        await DesktopUiWindow.RunWindow(paths, initParams, mainThread, connection, useTray,
            keepRunningWhileConnected: true, shell, startHidden, accessKey, isQuickLaunch, cancellationToken).Vhc();
    }

    // For the package's scheme, not for a person, so help leaves it out.
    private static Option<bool> CreateQuickLaunchOption()
    {
        return new Option<bool>("--quick-launch") {
            Description = "Connect as the Connect button does, unless connected already.",
            Hidden = true
        };
    }
}

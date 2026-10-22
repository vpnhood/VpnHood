using Microsoft.Extensions.Logging;
using VpnHood.AppLib.App;
using VpnHood.AppUi.Hosting.Desktop.Abstractions;
using VpnHood.AppUi.Hosting.Desktop.Commands;
using VpnHood.AppUi.Hosting.Desktop.Platform;
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
    // DesktopHost.Run. A window's process ends with its window: nothing cancels it from outside. No
    // argument is acted on yet; the log keeps them.
    public static int Run(string[] args, DesktopInitParams initParams, IInProcessDesktopPlatform platform)
    {
        using var mainThread = new MainThreadQueue();
        var run = Task.Run(() => RunAsync(args, initParams, platform, mainThread));
        mainThread.RunUntil(run);
        return run.GetAwaiter().GetResult();
    }

    private static async Task<int> RunAsync(string[] args, DesktopInitParams initParams,
        IInProcessDesktopPlatform platform, MainThreadQueue mainThread)
    {
        try {
            await using var uiInstance = DesktopUiInstance.TryClaim(platform.Paths, initParams.Ui);
            if (uiInstance == null)
                return 0;

            VhLogger.AddProvider(platform.CreateLoggerProvider());
            VhLogger.Instance.LogInformation("{InstanceName} started on .NET {Version}. Args: {Args}",
                platform.Paths.InstanceName, Environment.Version, string.Join(' ', args));

            try {
                await using var app = platform.CreateApp();
                await RunWindow(platform.Paths, platform.PeerCheck, initParams, mainThread, startHidden: false,
                    CancellationToken.None).Vhc();
                return 0;
            }
            catch (Exception ex) {
                // in the window's place: a person who clicked an icon has nowhere else to read it
                VhLogger.Instance.LogError(ex, "{InstanceName} could not run.", platform.Paths.InstanceName);
                await UiCommand.RunMessage(platform.Paths, initParams, mainThread, DesktopUiMessageKind.Failure,
                    ex.Message, CancellationToken.None).Vhc();
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
    internal static async Task RunWindow(IDesktopUiPaths paths, ILoopbackPeerCheck peerCheck,
        DesktopInitParams initParams, MainThreadQueue mainThread, bool startHidden, CancellationToken cancellationToken)
    {
        var localWebHost = VpnHoodApp.Instance.LocalWebHost ??
                           throw new InvalidOperationException(
                               "The app has no local web host, so the window could not reach it.");
        await localWebHost.EnsureStarted(cancellationToken).Vhc();

        // No channel to ask: the app is here, and so is the process the window's calls must reach.
        await using var connection = DaemonConnection.CreateInProcess(localWebHost, peerCheck, paths.InstanceName);
        await UiCommand.RunWindow(paths, initParams, mainThread, connection, startHidden, cancellationToken).Vhc();
    }
}

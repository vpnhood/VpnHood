using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.App.Linux;
using VpnHood.AppUi.Hosting.Desktop.Platform;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Desktop.Linux;

// The machine facts Linux answers. The installer registers the unit; systemd's signal stops the
// daemon, which the parser turns into the run's cancellation; the daemon's console is the journal where stdout is the journal, so no line
// reaches it twice; and the window writes to the journal's socket, under the instance's name, which
// "service log" reads beside the unit.
public sealed class LinuxDesktopPlatform : IDesktopPlatform
{
    private readonly LinuxDesktopPaths _paths;

    public LinuxDesktopPlatform(DesktopInitParams initParams)
    {
        _paths = new LinuxDesktopPaths(initParams.AppId);
        Instance = new LinuxInstanceController(_paths);
        Channel = new LinuxDaemonChannel(_paths);
        DaemonHostFactory = new LinuxDaemonHostFactory(initParams, _paths);
    }

    public IAppDesktopPaths Paths => _paths;
    public IAppInstanceController Instance { get; }
    public IDaemonChannel Channel { get; }
    public ILoopbackPeerCheck PeerCheck { get; } = new LinuxLoopbackPeerCheck();
    public IForegroundHandOff? ForegroundHandOff => null;
    public IAppInstanceSetup? InstanceSetup => null;
    public IAppDaemonHostFactory DaemonHostFactory { get; }

    public bool IsAdministrator() => LinuxAdministrators.IsCurrentUser();

    public ILoggerProvider CreateConsoleLoggerProvider() => LinuxJournalLogger.IsConsole
        ? new LinuxJournalLoggerProvider()
        : new ConsoleLoggerProvider();

    public ILoggerProvider CreateSystemLogLoggerProvider() => new LinuxJournalSocketLoggerProvider(_paths.InstanceName);

    public Task<int> HostDaemon(DaemonRun run, CancellationToken cancellationToken) =>
        DesktopHost.HostDaemonStoppedBySignal(run, cancellationToken);

    // As a unit of the person's systemd: a desktop runs the window as a unit of its own - the sign-in tray's is a
    // service (systemd-xdg-autostart-generator) - and a process the window left in it would end with the window.
    // Started directly where there is no such systemd.
    public void StartUiAgain(bool startHidden)
    {
        if (_paths.InstalledLauncherPath is not { } launcherPath)
            return;

        string[] uiArguments = startHidden ? ["ui", "--tray"] : ["ui"];
        try {
            using var systemdRun = Start("systemd-run", ["--user", "--collect", "--quiet", launcherPath, .. uiArguments]);
            systemdRun.WaitForExit();
            if (systemdRun.ExitCode == 0)
                return;

            VhLogger.Instance.LogWarning("systemd-run could not start the window again (exit {ExitCode}); starting it directly.",
                systemdRun.ExitCode);
        }
        catch (Win32Exception) {
            // no systemd-run here
        }

        Start(launcherPath, uiArguments).Dispose();
    }

    private static Process Start(string fileName, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo { FileName = fileName, UseShellExecute = false };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        return Process.Start(startInfo) ??
               throw new InvalidOperationException($"Could not run {fileName}.");
    }
}

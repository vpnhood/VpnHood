using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Device.Linux;
using VpnHood.Core.Common;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Linux.Common;

public class VpnHoodAppLinux : Singleton<VpnHoodAppLinux>
{
    private readonly CommandListener _commandListener;
    private const string FileNameAppCommand = "appcommand";
    private readonly bool _showWindowAfterStart;
    public event EventHandler? Exiting;

    public VpnHoodAppLinux(AppOptions appOptions, bool showWindowAfterStart)
    {
        _showWindowAfterStart = showWindowAfterStart;

        // init app
        VpnHoodApp.Init(new LinuxDevice(appOptions.StorageFolderPath), appOptions);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Exit();

        //create command Listener
        _commandListener = new CommandListener(Path.Combine(appOptions.StorageFolderPath, FileNameAppCommand));
        _commandListener.CommandReceived += CommandListener_CommandReceived;
        _commandListener.Start();
    }

    public static VpnHoodAppLinux Init(Func<AppOptions> optionsFactory, string[] args)
    {
        var autoConnect = args.Any(x => x.Equals("/autoconnect", StringComparison.OrdinalIgnoreCase));
        var showWindowAfterStart =
            !autoConnect && !args.Any(x => x.Equals("/nowindow", StringComparison.OrdinalIgnoreCase));
        var stop = args.Length > 0 && args[0].Equals("stop", StringComparison.OrdinalIgnoreCase);
        var appOptions = optionsFactory();
        Directory.CreateDirectory(appOptions.StorageFolderPath);

        // make sure only single instance is running
        // it must run before VpnHoodApp.Init to prevent internal system conflicts with previous instances
        VerifySingleInstance(appOptions, showWindowAfterStart, stop);

        // if stop was requested, just throw exception to stop the app
        if (stop)
            throw new GracefullyShutdownException();

        // create linux app
        var app = new VpnHoodAppLinux(appOptions, showWindowAfterStart);
        return app;
    }


    public async Task Run()
    {
        // try to remove the old adapter, as previous route maybe till be active
        await VhUtils.TryInvokeAsync(null, () =>
            ExecuteCommandAsync($"ip link delete {VpnHoodApp.Instance.Features.AppName}", CancellationToken.None));

        // show main window if requested
        if (_showWindowAfterStart)
            OpenMainWindowRequested?.Invoke(this, EventArgs.Empty);

        // wait until app is closed
        while (VpnHoodApp.IsInit)
            await Task.Delay(1000);
    }

    public event EventHandler? OpenMainWindowRequested;

    private void CommandListener_CommandReceived(object? sender, CommandReceivedEventArgs e)
    {
        // if stop command received, exit the app
        if (e.Arguments.Any(x => x.Equals("/stop", StringComparison.OrdinalIgnoreCase))) {
            VhLogger.Instance.LogInformation("Stop command has been received.");
            Exit();
            return;
        }

        if (e.Arguments.Any(x => x.Equals("/openwindow", StringComparison.OrdinalIgnoreCase)))
            OpenMainWindowRequested?.Invoke(this, EventArgs.Empty);
    }

    private static void VerifySingleInstance(AppOptions appOptions, bool showWindow, bool stop)
    {
        if (!IsAnotherInstanceRunning(appOptions))
            return;

        var command = "";
        if (stop) command += "/stop ";
        else if (showWindow) command += "/openWindow ";

        // Make single instance
        // if you like to wait a few seconds in case that the instance is just shutting down
        // open main window if app is already running and user run the app again
        if (!string.IsNullOrWhiteSpace(command)) {
            var commandListener = new CommandListener(Path.Combine(appOptions.StorageFolderPath, FileNameAppCommand));
            VhUtils.TryInvoke(null, () => commandListener.SendCommand(command));
        }

        throw stop
            ? new GracefullyShutdownException()
            : new AnotherInstanceIsRunningException();
    }

    private static Socket? _singleInstanceSocket;

    private static bool IsAnotherInstanceRunning(AppOptions appOptions)
    {
        // Linux-only: abstract UDS address starts with '\0'
        _singleInstanceSocket = new Socket(AddressFamily.Unix, SocketType.Stream, 0);

        try {
            var abstractName = "\0singleton-" + appOptions.AppId;
            _singleInstanceSocket.Bind(new UnixDomainSocketEndPoint(abstractName));
            _singleInstanceSocket.Listen(1);
            return false; // No other instance running
        }
        catch {
            _singleInstanceSocket.Dispose();
            return true; // Fallback: treat any unexpected error as "already running"
        }
    }

    public void Exit()
    {
        Exiting?.Invoke(this, EventArgs.Empty);
        Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            if (VpnHoodApp.IsInit) VpnHoodApp.Instance.Dispose();
            _singleInstanceSocket?.Dispose();
        }

        base.Dispose(disposing);
    }

    private static Task<string> ExecuteCommandAsync(string command, CancellationToken cancellationToken)
    {
        return OsUtils.ExecuteCommandAsync("/bin/bash", $"-c \"{command}\"", cancellationToken);
    }
}
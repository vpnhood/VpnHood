using VpnHood.AppLib;
using VpnHood.AppLib.WebServer;
using VpnHood.Core.Client.Device.Linux;
using VpnHood.Core.Common;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.App.Client.Linux.Web;

public class VpnHoodAppLinux : Singleton<VpnHoodAppLinux>
{
    private readonly CommandListener _commandListener;
    private const string FileNameAppCommand = "appcommand";
    private readonly bool _showWindowAfterStart;

    public VpnHoodAppLinux(AppOptions appOptions, bool showWindowAfterStart)
    {
        _showWindowAfterStart = showWindowAfterStart;

        // init app
        VpnHoodApp.Init(new LinuxDevice(appOptions.StorageFolderPath), appOptions);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Dispose();

        //create command Listener
        _commandListener = new CommandListener(Path.Combine(appOptions.StorageFolderPath, FileNameAppCommand));
        _commandListener.CommandReceived += CommandListener_CommandReceived;
        _commandListener.Start();
    }

    public static VpnHoodAppLinux Init(Func<AppOptions> optionsFactory, string[] args)
    {
        var autoConnect = args.Any(x => x.Equals("/autoconnect", StringComparison.OrdinalIgnoreCase));
        var showWindowAfterStart = !autoConnect && !args.Any(x => x.Equals("/nowindow", StringComparison.OrdinalIgnoreCase));
        var appOptions = optionsFactory();

        // make sure only single instance is running
        // it must run before VpnHoodApp.Init to prevent internal system conflicts with previous instances
        VerifySingleInstance(appOptions, showWindowAfterStart);

        // create linux app
        var app = new VpnHoodAppLinux(appOptions, showWindowAfterStart);
        return app;
    }


    public async Task Run()
    {
        // try to remove the old adapter, as previous route may till be active
        await VhUtils.TryInvokeAsync(null, () =>
            ExecuteCommandAsync($"ip link delete {AppConfigs.AppName}", CancellationToken.None));

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
        if (e.Arguments.Any(x => x.Equals("/openwindow", StringComparison.OrdinalIgnoreCase)))
            OpenMainWindowRequested?.Invoke(this, EventArgs.Empty);
    }

    private static void VerifySingleInstance(AppOptions appOptions, bool showWindow)
    {
        if (!IsAnotherInstanceRunning(appOptions))
            return;

        // Make single instance
        // if you like to wait a few seconds in case that the instance is just shutting down
        // open main window if app is already running and user run the app again
        if (showWindow) {
            var commandListener = new CommandListener(Path.Combine(appOptions.StorageFolderPath, FileNameAppCommand));
            commandListener.SendCommand("/openWindow");
        }

        throw new Exception("VpnHood client is already running.");
    }

    private static bool IsAnotherInstanceRunning(AppOptions appOptions)
    {
        var lockFilePath = Path.Combine(Path.Combine(appOptions.StorageFolderPath, $"{appOptions.AppId}.lock"));

        try {
            _ = new FileStream(
                lockFilePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None
            );
            return false; // got the lock
        }
        catch (IOException) {
            return true; // someone else holds it
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            if (VpnHoodAppWebServer.IsInit) VpnHoodAppWebServer.Instance.Dispose();
            if (VpnHoodApp.IsInit) VpnHoodApp.Instance.DisposeAsync().AsTask().Wait(cts.Token);
        }

        base.Dispose(disposing);
    }

    private static Task<string> ExecuteCommandAsync(string command, CancellationToken cancellationToken)
    {
        return OsUtils.ExecuteCommandAsync("/bin/bash", $"-c \"{command}\"", cancellationToken);
    }
}
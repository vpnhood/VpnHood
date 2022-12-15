using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using VpnHood.Client.App.UI;
using VpnHood.Common;
using VpnHood.Common.Logging;

namespace VpnHood.Client.App;

public class WinApp : AppBaseNet<WinApp>
{
    private const string FileNameAppCommand = "appcommand";
    private readonly System.Windows.Forms.Timer _uiTimer;
    private DateTime _lastUpdateTime = DateTime.MinValue;
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private readonly CommandListener _commandListener;
    private WebViewWindow? _webViewWindow;
    private string AppLocalDataPath { get; }

    public WinApp() : base("VpnHood")
    {
        //init timer
        _uiTimer = new System.Windows.Forms.Timer
        {
            Interval = 1000
        };
        _uiTimer.Tick += (_, _) => UpdateNotifyIconText();
        AppLocalDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);

        //create command Listener
        _commandListener = new CommandListener(Path.Combine(AppLocalDataPath, FileNameAppCommand));
        _commandListener.CommandReceived += CommandListener_CommandReceived;
    }

    private void CommandListener_CommandReceived(object? sender, CommandReceivedEventArgs e)
    {
        if (e.Arguments.Any(x => x.Equals("/openwindow", StringComparison.OrdinalIgnoreCase)))
            OpenMainWindow();
    }

    public TimeSpan UpdateInterval { get; set; } = TimeSpan.FromDays(1);
    private static VpnHoodApp VhApp => VpnHoodApp.Instance;
    private static VpnHoodAppUi VhAppUi => VpnHoodAppUi.Instance;

    protected override void OnStart(string[] args)
    {
        const bool logToConsole = true;
        var autoConnect = args.Any(x => x.Equals("/autoconnect", StringComparison.OrdinalIgnoreCase));
        var showWindow = !autoConnect && !args.Any(x => x.Equals("/nowindow", StringComparison.OrdinalIgnoreCase));
        // Make single instance
        // if you like to wait a few seconds in case that the instance is just shutting down
        if (IsAnotherInstanceRunning())
        {
            // open main window if app is already running and user run the app again
            if (showWindow)
                _commandListener.SendCommand("/openWindow");
            VhLogger.Instance.LogInformation($"{nameof(WinApp)} is already running!");
            return;
        }

        // configuring Windows Firewall
        try
        {
            OpenLocalFirewall(AppLocalDataPath);
        }
        catch
        {
            /*ignored*/
        }

        // init app
        VpnHoodApp.Init(new WinAppProvider(), new AppOptions { LogToConsole = logToConsole, AppDataPath = AppLocalDataPath });
        VpnHoodAppUi.Init(new MemoryStream(Resource.SPA));

        // auto connect
        if (autoConnect &&
            VhApp.UserSettings.DefaultClientProfileId != null &&
            VhApp.ClientProfileStore.ClientProfileItems.Any(x =>
                x.ClientProfile.ClientProfileId == VhApp.UserSettings.DefaultClientProfileId))
            _ = VhApp.Connect(VhApp.UserSettings.DefaultClientProfileId.Value);

        // create notification icon
        InitNotifyIcon();

        // Create webview if installed
        InitWevView();

        // MainWindow
        if (showWindow)
            OpenMainWindow();

        //Ui Timer
        _uiTimer.Enabled = true;
        _commandListener.Start();

        // Message Loop
        Application.ApplicationExit += App_Exit;
        Application.Run();
    }

    private void App_Exit(object? sender, EventArgs e)
    {
        _webViewWindow?.Close();
    }

    private void UpdateNotifyIconText()
    {
        var stateName = VhApp.State.ConnectionState == AppConnectionState.None
            ? "Disconnected"
            : VhApp.State.ConnectionState.ToString();

        if (_notifyIcon != null)
        {
            _notifyIcon.Text = $@"{AppUiResource.AppName} - {stateName}";
            if (VhApp.State.IsIdle) _notifyIcon.Icon = Resource.VpnDisconnectedIcon;
            else if (VhApp.State.ConnectionState == AppConnectionState.Connected)
                _notifyIcon.Icon = Resource.VpnConnectedIcon;
            else _notifyIcon.Icon = Resource.VpnConnectingIcon;
        }

        CheckForUpdate();
    }

    public void InitWevView()
    {
        try
        {
            if (!WebViewWindow.IsInstalled) return;
            _webViewWindow = new WebViewWindow(new Uri(VhAppUi.Url), Path.Combine(VhApp.AppDataFolderPath, "Temp"));

            //_webViewWindow.Init("https://www.google.com", @"C:\Users\Developer\AppData\Local\VpnHood\Temp\dd").Wait();
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogWarning($"Could not use WebView. Using the default browser. {ex.Message}");
        }
    }


    private void CheckForUpdate()
    {
        // read last check
        var lastCheckFilePath = Path.Combine(VhApp.AppDataFolderPath, "lastCheckUpdate");
        if (_lastUpdateTime == DateTime.MinValue && File.Exists(lastCheckFilePath))
            try
            {
                _lastUpdateTime = JsonSerializer.Deserialize<DateTime>(File.ReadAllText(lastCheckFilePath));
            }
            catch
            {
                /*Ignored*/
            }

        // check last update time
        if (DateTime.Now - _lastUpdateTime < UpdateInterval)
            return;

        // set updateTime before checking filename
        _lastUpdateTime = DateTime.Now;

        // launch updater if exists
        var assemblyLocation = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location) ??
                               throw new Exception("Could not get the parent of Assembly location!");
        var updaterFilePath = Path.Combine(assemblyLocation, "updater.exe");
        if (!File.Exists(updaterFilePath))
        {
            VhLogger.Instance.LogWarning($"Could not find updater: {updaterFilePath}");
            return;
        }

        try
        {
            VhLogger.Instance.LogInformation("Checking for new updates...");
            Process.Start(updaterFilePath, "/silent");
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex.Message);
        }
        finally
        {
            File.WriteAllText(lastCheckFilePath, JsonSerializer.Serialize(_lastUpdateTime));
        }
    }

    public void OpenMainWindow()
    {
        if (_webViewWindow != null)
        {
            _webViewWindow.Show();
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = VhAppUi.Url,
            UseShellExecute = true,
            Verb = "open"
        });
    }

    private void InitNotifyIcon()
    {
        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = Resource.VpnHoodIcon
        };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
                OpenMainWindow();
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add(AppUiResource.Open, null, (_, _) => OpenMainWindow());

        menu.Items.Add("-");
        var menuItem = menu.Items.Add("Connect");
        menuItem.Name = "connect";
        menuItem.Click += ConnectMenuItem_Click;

        menuItem = menu.Items.Add(AppUiResource.Disconnect);
        menuItem.Name = "disconnect";
        menuItem.Click += (_, _) => VhApp.Disconnect(true);

        menu.Items.Add("-");
        menu.Items.Add(AppUiResource.Exit, null, (_, _) => Application.Exit());
        menu.Opening += Menu_Opening;
        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.Text = AppUiResource.AppName;
        _notifyIcon.Visible = true;

        UpdateNotifyIconText();
    }

    private void ConnectMenuItem_Click(object? sender, EventArgs e)
    {
        if (VhApp.Settings.UserSettings.DefaultClientProfileId != null)
            try
            {
                _ = VhApp.Connect(VhApp.Settings.UserSettings.DefaultClientProfileId.Value);
            }
            catch
            {
                OpenMainWindow();
            }
        else
            OpenMainWindow();
    }

    private void Menu_Opening(object? sender, CancelEventArgs e)
    {
        var menu = (System.Windows.Forms.ContextMenuStrip)sender!;
        menu.Items["connect"].Enabled = VhApp.IsIdle;
        menu.Items["disconnect"].Enabled =
            !VhApp.IsIdle && VhApp.State.ConnectionState != AppConnectionState.Disconnecting;
    }

    private static string FindExePath(string exe)
    {
        exe = Environment.ExpandEnvironmentVariables(exe);
        if (File.Exists(exe))
            return Path.GetFullPath(exe);

        if (Path.GetDirectoryName(exe) == string.Empty)
            foreach (var test in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                var path = test.Trim();
                if (!string.IsNullOrEmpty(path) && File.Exists(path = Path.Combine(path, exe)))
                    return Path.GetFullPath(path);
            }

        throw new FileNotFoundException(new FileNotFoundException().Message, exe);
    }

    private static void OpenLocalFirewall(string appDataPath)
    {
        var lastFirewallConfig = Path.Combine(appDataPath, "lastFirewallConfig");
        var lastExeFile = File.Exists(lastFirewallConfig) ? File.ReadAllText(lastFirewallConfig) : null;
        var curExePath = Path.ChangeExtension(typeof(WinApp).Assembly.Location, "exe");
        if (lastExeFile == curExePath)
            return;

        VhLogger.Instance.LogInformation("Configuring Windows Defender Firewall...");
        var ruleName = "VpnHood";

        //dotnet exe
        var exePath = FindExePath("dotnet.exe");
        ProcessStartNoWindow("netsh", $"advfirewall firewall delete rule name=\"{ruleName}\" dir=in").WaitForExit();
        ProcessStartNoWindow("netsh", $"advfirewall firewall add rule  name=\"{ruleName}\" program=\"{exePath}\" protocol=TCP localport=any action=allow profile=any dir=in")
            .WaitForExit();
        ProcessStartNoWindow("netsh", $"advfirewall firewall add rule  name=\"{ruleName}\" program=\"{exePath}\" protocol=UDP localport=any action=allow profile=any dir=in")
            .WaitForExit();

        // VpnHood exe
        exePath = curExePath;
        if (File.Exists(exePath))
        {
            ProcessStartNoWindow("netsh", $"advfirewall firewall add rule  name=\"{ruleName}\" program=\"{exePath}\" protocol=TCP localport=any action=allow profile=any dir=in")
                .WaitForExit();
            ProcessStartNoWindow("netsh", $"advfirewall firewall add rule  name=\"{ruleName}\" program=\"{exePath}\" protocol=UDP localport=any action=allow profile=any dir=in")
                .WaitForExit();
        }

        // save firewall modified
        File.WriteAllText(lastFirewallConfig, curExePath);
    }

    private static Process ProcessStartNoWindow(string filename, string argument)
    {
        var processStart = new ProcessStartInfo(filename, argument)
        {
            CreateNoWindow = true
        };

        return Process.Start(processStart) ?? throw new Exception($"Could not start process: {filename}");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !Disposed)
        {
            _uiTimer.Dispose();
            _notifyIcon?.Dispose();
            if (VpnHoodAppUi.IsInit) VhAppUi.Dispose();
            if (VpnHoodApp.IsInit) VhApp.Dispose();
        }

        Disposed = true;

        // base
        base.Dispose(disposing);
    }
}
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Client.App.Maui.Resources;
using VpnHood.Client.App.UI;
using VpnHood.Common;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;
using WinNative;

// ReSharper disable once CheckNamespace
namespace VpnHood.Client.App;

public class WinApp : IDisposable
{
    private const string DefaultLocalHost = "myvpnhood";
    private readonly IPEndPoint _defaultLocalHostEndPoint = IPEndPoint.Parse("127.10.10.10:80");
    private const string FileNameAppCommand = "appcommand";
    private Mutex? _instanceMutex;
    private readonly Timer _uiTimer;
    private DateTime _lastUpdateTime = DateTime.MinValue;
    private readonly SystemTray _sysTray;
    private readonly CommandListener _commandListener;
    private bool _disposed;
    private bool _showWindowAfterStart;
    private readonly string _appLocalDataPath;
    private readonly IntPtr _disconnectedIconHandle;
    private readonly IntPtr _connectedIconHandle;
    private readonly IntPtr _connectingIconHandle;

    public event EventHandler? OpenWindowRequested;
    public event EventHandler? ExitRequested;

    public WinApp()
    {
        // console logger
        VhLogger.Instance = VhLogger.CreateConsoleLogger();
        _disconnectedIconHandle = SystemTray.LoadImage(IntPtr.Zero, Path.Combine(AppContext.BaseDirectory, "Icons", "VpnDisconnected.ico"), 1, 16, 16, 0x00000010);
        _connectedIconHandle = SystemTray.LoadImage(IntPtr.Zero, Path.Combine(AppContext.BaseDirectory, "Icons", "VpnConnected.ico"), 1, 16, 16, 0x00000010);
        _connectingIconHandle = SystemTray.LoadImage(IntPtr.Zero, Path.Combine(AppContext.BaseDirectory, "Icons", "VpnConnecting.ico"), 1, 16, 16, 0x00000010);
        _sysTray = new SystemTray("VpnHood!", _disconnectedIconHandle);

        //init timer
        _uiTimer = new Timer(UpdateNotifyIconText, null, 1000, 1000);
        _appLocalDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VpnHood");

        //create command Listener
        _commandListener = new CommandListener(Path.Combine(_appLocalDataPath, FileNameAppCommand));
        _commandListener.CommandReceived += CommandListener_CommandReceived;
    }

    private void CommandListener_CommandReceived(object? sender, CommandReceivedEventArgs e)
    {
        if (e.Arguments.Any(x => x.Equals("/openwindow", StringComparison.OrdinalIgnoreCase)))
            OpenMainWindow();
    }

    public TimeSpan UpdateInterval { get; set; } = TimeSpan.FromDays(1);
    private static VpnHoodApp VhApp => VpnHoodApp.Instance;

    public bool IsAnotherInstanceRunning(string? name = null)
    {
        name ??= typeof(WinApp).FullName;
        _instanceMutex ??= new Mutex(false, name);

        // Make single instance
        // if you like to wait a few seconds in case that the instance is just shutting down
        return !_instanceMutex.WaitOne(TimeSpan.FromSeconds(0), false);
    }

    public bool Start(string[] args)
    {
        var autoConnect = args.Any(x => x.Equals("/autoconnect", StringComparison.OrdinalIgnoreCase));
        _showWindowAfterStart = !autoConnect && !args.Any(x => x.Equals("/nowindow", StringComparison.OrdinalIgnoreCase));

        // Make single instance
        // if you like to wait a few seconds in case that the instance is just shutting down
        if (IsAnotherInstanceRunning())
        {
            // open main window if app is already running and user run the app again
            if (_showWindowAfterStart)
                _commandListener.SendCommand("/openWindow");
            VhLogger.Instance.LogInformation("WinApp is already running!");
            return false;
        }

        // configure local host
        var localWebUrl = RegisterLocalDomain();

        // configuring Windows Firewall
        try
        {
            OpenLocalFirewall(_appLocalDataPath);
        }
        catch
        {
            /*ignored*/
        }

        // init app
        //VpnHoodApp.Init(new WinAppProvider(), new AppOptions
        //{
        //    IsLogToConsoleSupported = true,
        //    AppDataPath = AppLocalDataPath,
        //    UpdateInfoUrl = new Uri("https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodClient-win-x64.json")
        //});
        //VpnHoodAppUi.Init(new MemoryStream(Resource.SPA), url2: localWebUrl);

        // auto connect
        if (autoConnect &&
            VhApp.UserSettings.DefaultClientProfileId != null &&
            VhApp.ClientProfileStore.ClientProfileItems.Any(x =>
                x.ClientProfile.ClientProfileId == VhApp.UserSettings.DefaultClientProfileId))
            _ = VhApp.Connect(VhApp.UserSettings.DefaultClientProfileId.Value);

        // create notification icon
        InitNotifyIcon();
        OpenMainWindow();

        _commandListener.Start();

        return true;
    }

    private void UpdateNotifyIconText(object? state)
    {
        var stateName = VhApp.State.ConnectionState == AppConnectionState.None
            ? "Disconnected" //todo localize
            : VhApp.State.ConnectionState.ToString();

        var hIcon = _connectingIconHandle;
        if (VhApp.State.ConnectionState == AppConnectionState.Connected) hIcon = _connectedIconHandle;
        else if (VhApp.IsIdle) hIcon = _disconnectedIconHandle;

        _sysTray.Update($@"{AppUiResource.AppName} - {stateName}", hIcon);

        CheckForUpdate();
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
        if (FastDateTime.Now - _lastUpdateTime < UpdateInterval)
            return;

        // set updateTime before checking filename
        _lastUpdateTime = FastDateTime.Now;

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

    private void OpenMainWindow()
    {
        OpenWindowRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Exit()
    {
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    public void OpenMainWindowInBrowser()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = VpnHoodAppUi.Instance.Url.AbsoluteUri,
            UseShellExecute = true,
            Verb = "open"
        });

    }

    private void InitNotifyIcon()
    {
        _sysTray.Clicked += (_, _) =>
        {
            OpenMainWindow();
        };

        _sysTray.ContextMenu = new ContextMenu();
        _sysTray.ContextMenu.AddMenuItem(UiResource.Open, (_, _) => OpenMainWindow());
        _sysTray.ContextMenu.AddMenuItem(UiResource.OpenInBrowser, (_, _) => OpenMainWindowInBrowser());
        _sysTray.ContextMenu.AddMenuSeparator();
        _sysTray.ContextMenu.AddMenuItem(UiResource.Connect, (_, _) => ConnectClicked());
        _sysTray.ContextMenu.AddMenuItem(UiResource.Disconnect, (_, _) => _ = VhApp.Disconnect(true));
        _sysTray.ContextMenu.AddMenuSeparator();
        _sysTray.ContextMenu.AddMenuItem(UiResource.Exit, (_, _) => Exit());
    }

    private void ConnectClicked()
    {
        if (VhApp.Settings.UserSettings.DefaultClientProfileId != null)
        {
            try
            {
                _ = VhApp.Connect(VhApp.Settings.UserSettings.DefaultClientProfileId.Value);
            }
            catch
            {
                OpenMainWindow();
            }
        }
        else
        {
            OpenMainWindow();
        }
    }

    //private void Menu_Opening(object? sender, CancelEventArgs e)
    //{
    //    var menu = (ContextMenuStrip)sender!;
    //    menu.Items["connect"].Enabled = VhApp.IsIdle;
    //    menu.Items["disconnect"].Enabled = !VhApp.IsIdle && VhApp.State.ConnectionState != AppConnectionState.Disconnecting;
    //    menu.Items["open"].Visible = _webViewWindow is { IsInitCompleted: true };
    //    menu.Items["openInBrowser"].Visible = true;
    //}

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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _uiTimer.Dispose();
        //_notifyIcon?.Dispose();
        //_webViewWindow?.Close();
        if (VpnHoodAppUi.IsInit) VpnHoodAppUi.Instance.Dispose();
        if (VpnHoodApp.IsInit) _ = VhApp.DisposeAsync();
    }

    private Uri? RegisterLocalDomain()
    {
        // check default ip
        IPEndPoint? freeLocalEndPoint = null;
        try
        {
            freeLocalEndPoint = VhUtil.GetFreeTcpEndPoint(_defaultLocalHostEndPoint.Address, _defaultLocalHostEndPoint.Port);
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError("Could not find free port local host. LocalIp:{LocalIp}, Message: {Message}",
                _defaultLocalHostEndPoint.Address, ex.Message);
        }

        // check 127.0.0.1
        if (freeLocalEndPoint == null)
        {
            try
            {
                freeLocalEndPoint = VhUtil.GetFreeTcpEndPoint(IPAddress.Loopback, 9090);
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogError("Could not find free port local host. LocalIp:{LocalIp}, Message: {Message}",
                    IPAddress.Loopback, ex.Message);
                return null;
            }
        }

        try
        {
            var hostsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");
            var hostLines = File.ReadLines(hostsFilePath).ToList();

            // remove wrong items
            var newHostLines = hostLines
                .Where(x =>
                {
                    var items = x.Split(" ");
                    var isWrongVpnHoodLine =
                        items.Length > 1 &&
                        items[0].Trim() != freeLocalEndPoint.Address.ToString() &&
                        items[1].Trim().Equals(DefaultLocalHost, StringComparison.OrdinalIgnoreCase);
                    return !isWrongVpnHoodLine;
                })
                .ToList();

            // add item if does not exists
            var isAlreadyAdded = newHostLines
                .Any(x =>
                {
                    var items = x.Split(" ");
                    var isVpnHoodLine =
                        items.Length > 1 &&
                        items[0].Trim() == freeLocalEndPoint.Address.ToString() &&
                        items[1].Trim().Equals(DefaultLocalHost, StringComparison.OrdinalIgnoreCase);
                    return isVpnHoodLine;
                });

            if (!isAlreadyAdded)
                newHostLines.Add($"{freeLocalEndPoint.Address} {DefaultLocalHost} # Added by VpnHood!");

            // update if changed
            if (!hostLines.SequenceEqual(newHostLines))
                File.WriteAllLines(hostsFilePath, newHostLines.ToArray());

            return new Uri($"http://{DefaultLocalHost}:{freeLocalEndPoint.Port}");
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Could not register local domain.");
            return null;
        }

    }
}
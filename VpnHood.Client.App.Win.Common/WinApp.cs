using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Common;
using VpnHood.Common.JobController;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;
using WinNative;

namespace VpnHood.Client.App.Win.Common;

public class WinApp : IDisposable, IJob
{
    private const string FileNameAppCommand = "appcommand";
    private Mutex? _instanceMutex;
    private SystemTray? _sysTray;
    private DateTime _lastUpdateTime = DateTime.MinValue;
    private readonly CommandListener _commandListener;
    private bool _disposed;
    private readonly string _appLocalDataPath;
    private readonly Icon _appIcon;
    private Icon? _disconnectedIcon;
    private Icon? _connectedIcon;
    private Icon? _connectingIcon;
    private int _connectMenuItemId;
    private int _disconnectMenuItemId;
    private int _openMainWindowMenuItemId;
    private int _openMainWindowInBrowserMenuItemId;
    private static VpnHoodApp VhApp => VpnHoodApp.Instance;
    private static readonly Lazy<WinApp> InstanceFiled = new(() => new WinApp());

    public event EventHandler? OpenMainWindowRequested;
    public event EventHandler? OpenMainWindowInBrowserRequested;
    public event EventHandler? ExitRequested;
    public TimeSpan CheckUpdateInterval { get; set; } = TimeSpan.FromHours(24);
    public JobSection JobSection { get; } = new();
    public static WinApp Instance => InstanceFiled.Value;
    public bool ShowWindowAfterStart { get; private set; }
    public bool ConnectAfterStart { get; private set; }
    public bool EnableOpenMainWindow { get; set; } = true;

    private WinApp()
    {
        VhLogger.Instance = VhLogger.CreateConsoleLogger();
        _appLocalDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VpnHood");
        _appIcon = Icon.ExtractAssociatedIcon(Assembly.GetEntryAssembly()?.Location ?? throw new Exception("Could not get the location of Assembly."))
            ?? throw new Exception("Could not get the icon of the executing assembly.");

        //create command Listener
        _commandListener = new CommandListener(Path.Combine(_appLocalDataPath, FileNameAppCommand));
        _commandListener.CommandReceived += CommandListener_CommandReceived;

        // add to job runner
        JobRunner.Default.Add(this);
    }

    [DllImport("DwmApi")]
    private static extern int DwmSetWindowAttribute(IntPtr hWnd, int attr, int[] attrValue, int attrSize);
    public static void SetWindowTitleBarColor(IntPtr hWnd, Color color)
    {
        var attrValue = new[] { color.B << 16 | color.G << 8 | color.R };
        const int captionColor = 35;
        DwmSetWindowAttribute(hWnd, captionColor, attrValue, attrValue.Length * 4);
    }

    public static void OpenUrlInExternalBrowser(Uri url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url.AbsoluteUri,
            UseShellExecute = true,
            Verb = "open"
        });
    }

    private void CommandListener_CommandReceived(object? sender, CommandReceivedEventArgs e)
    {
        if (e.Arguments.Any(x => x.Equals("/openwindow", StringComparison.OrdinalIgnoreCase)))
            OpenMainWindow();
    }

    public bool IsAnotherInstanceRunning(string? name = null)
    {
        name ??= typeof(WinApp).FullName;
        _instanceMutex ??= new Mutex(false, name);

        // Make single instance
        // if you like to wait a few seconds in case that the instance is just shutting down
        return !_instanceMutex.WaitOne(TimeSpan.FromSeconds(0), false);
    }

    public void PreStart(string[] args)
    {
        ConnectAfterStart = args.Any(x => x.Equals("/autoconnect", StringComparison.OrdinalIgnoreCase));
        ShowWindowAfterStart = !ConnectAfterStart && !args.Any(x => x.Equals("/nowindow", StringComparison.OrdinalIgnoreCase));

        // Make single instance
        // if you like to wait a few seconds in case that the instance is just shutting down
        if (IsAnotherInstanceRunning())
        {
            // open main window if app is already running and user run the app again
            if (ShowWindowAfterStart)
                _commandListener.SendCommand("/openWindow");
            throw new Exception("VpnHood client is already running.");
        }

        // configuring Windows Firewall
        try
        {
            OpenLocalFirewall(_appLocalDataPath);
        }
        catch
        {
            /*ignored*/
        }
    }

    public bool Start()
    {
        // auto connect
        if (ConnectAfterStart &&
            VhApp.UserSettings.DefaultClientProfileId != null &&
            VhApp.ClientProfileStore.ClientProfileItems.Any(x =>
                x.ClientProfile.ClientProfileId == VhApp.UserSettings.DefaultClientProfileId))
            _ = VhApp.Connect(VhApp.UserSettings.DefaultClientProfileId.Value);

        // check for update
        CheckForUpdate();

        // create notification icon
        InitNotifyIcon();
        VhApp.ConnectionStateChanged += (_, _) => UpdateNotifyIcon();
        UpdateNotifyIcon();

        // start command listener
        _commandListener.Start();
        return true;
    }

    private void InitNotifyIcon()
    {
        _sysTray = new SystemTray(VhApp.Resources.Strings.AppName, _appIcon.Handle);
        _sysTray.Clicked += (_, _) => OpenMainWindow();
        _sysTray.ContextMenu = new ContextMenu();
        _openMainWindowMenuItemId = _sysTray.ContextMenu.AddMenuItem(VhApp.Resources.Strings.Open, (_, _) => OpenMainWindow());
        _openMainWindowInBrowserMenuItemId = _sysTray.ContextMenu.AddMenuItem(VhApp.Resources.Strings.OpenInBrowser, (_, _) => OpenMainWindowInBrowser());
        _sysTray.ContextMenu.AddMenuSeparator();
        _connectMenuItemId = _sysTray.ContextMenu.AddMenuItem(VhApp.Resources.Strings.Connect, (_, _) => ConnectClicked());
        _disconnectMenuItemId = _sysTray.ContextMenu.AddMenuItem(VhApp.Resources.Strings.Disconnect, (_, _) => _ = VhApp.Disconnect(true));
        _sysTray.ContextMenu.AddMenuSeparator();
        _sysTray.ContextMenu.AddMenuItem(VhApp.Resources.Strings.Exit, (_, _) => Exit());

        // initialize icons
        if (VhApp.Resources.Icons.SystemTrayConnectingIcon!=null)
            _connectingIcon = new Icon(new MemoryStream(VhApp.Resources.Icons.SystemTrayConnectingIcon.Data));

        if (VhApp.Resources.Icons.SystemTrayConnectedIcon != null)
            _connectedIcon  = new Icon(new MemoryStream(VhApp.Resources.Icons.SystemTrayConnectedIcon.Data));

        if (VhApp.Resources.Icons.SystemTrayDisconnectedIcon != null)
            _disconnectedIcon = new Icon(new MemoryStream(VhApp.Resources.Icons.SystemTrayDisconnectedIcon.Data));
    }

    private void UpdateNotifyIcon()
    {
        if (!VpnHoodApp.IsInit || _sysTray == null)
            return;

        // update icon and text
        var stateName = VhApp.State.ConnectionState == AppConnectionState.None
            ? VhApp.Resources.Strings.Disconnected
            : VhApp.State.ConnectionState.ToString();

        var icon = _connectingIcon;
        if (VhApp.State.ConnectionState == AppConnectionState.Connected) icon = _connectedIcon;
        else if (VhApp.IsIdle) icon = _disconnectedIcon;
        icon ??= _appIcon;

        _sysTray.Update($@"{VhApp.Resources.Strings.AppName} - {stateName}", icon.Handle);
        _sysTray.ContextMenu?.EnableMenuItem(_connectMenuItemId, VhApp.IsIdle);
        _sysTray.ContextMenu?.EnableMenuItem(_connectMenuItemId, VhApp.IsIdle);
        _sysTray.ContextMenu?.EnableMenuItem(_disconnectMenuItemId, !VhApp.IsIdle && VhApp.State.ConnectionState != AppConnectionState.Disconnecting);
        _sysTray.ContextMenu?.EnableMenuItem(_openMainWindowMenuItemId, EnableOpenMainWindow);
        _sysTray.ContextMenu?.EnableMenuItem(_openMainWindowInBrowserMenuItemId, true);
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
        if (FastDateTime.Now - _lastUpdateTime < CheckUpdateInterval)
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
        if (EnableOpenMainWindow)
            OpenMainWindowRequested?.Invoke(this, EventArgs.Empty);
        else
            OpenMainWindowInBrowser();
    }

    public void OpenMainWindowInBrowser()
    {
        OpenMainWindowInBrowserRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Exit()
    {
        ExitRequested?.Invoke(this, EventArgs.Empty);
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
        var curExePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(curExePath) || lastExeFile == curExePath)
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

        _sysTray?.Dispose();
    }

    public static Uri? RegisterLocalDomain(IPEndPoint hostEndPoint, string localHost)
    {
        // check default ip
        IPEndPoint? freeLocalEndPoint = null;
        try
        {
            freeLocalEndPoint = VhUtil.GetFreeTcpEndPoint(hostEndPoint.Address, hostEndPoint.Port);
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError("Could not find free port local host. LocalIp:{LocalIp}, Message: {Message}",
                hostEndPoint.Address, ex.Message);
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
                        items[1].Trim().Equals(localHost, StringComparison.OrdinalIgnoreCase);
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
                        items[1].Trim().Equals(localHost, StringComparison.OrdinalIgnoreCase);
                    return isVpnHoodLine;
                });

            if (!isAlreadyAdded)
                newHostLines.Add($"{freeLocalEndPoint.Address} {localHost} # Added by VpnHood!");

            // update if changed
            if (!hostLines.SequenceEqual(newHostLines))
                File.WriteAllLines(hostsFilePath, newHostLines.ToArray());

            return new Uri($"http://{localHost}:{freeLocalEndPoint.Port}");
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Could not register local domain.");
            return null;
        }
    }

    public Task RunJob()
    {
        CheckForUpdate();
        return Task.CompletedTask;
    }

}
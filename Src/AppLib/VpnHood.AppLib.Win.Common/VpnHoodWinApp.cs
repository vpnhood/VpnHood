using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Common;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Utils;
using WinNative;

namespace VpnHood.AppLib.Win.Common;

public class VpnHoodWinApp : Singleton<VpnHoodWinApp>, IDisposable
{
    private readonly string _appId;
    private const string FileNameAppCommand = "appcommand";
    private Mutex? _instanceMutex;
    private SystemTray? _sysTray;
    private readonly CommandListener _commandListener;
    private bool _disposed;
    private readonly string _storageFolder;
    private readonly Icon _appIcon;
    private Icon? _disconnectedIcon;
    private Icon? _connectedIcon;
    private Icon? _connectingIcon;
    private int _connectMenuItemId;
    private int _disconnectMenuItemId;
    private int _openMainWindowMenuItemId;
    private int _openMainWindowInBrowserMenuItemId;

    public event EventHandler? OpenMainWindowRequested;
    public event EventHandler? OpenMainWindowInBrowserRequested;
    public event EventHandler? ExitRequested;
    public bool ShowWindowAfterStart { get; private set; }
    public bool ConnectAfterStart { get; private set; }
    public bool EnableOpenMainWindow { get; set; } = true;
    
    [DllImport("DwmApi")]
    private static extern int DwmSetWindowAttribute(IntPtr hWnd, int attr, int[] attrValue, int attrSize);


    private VpnHoodWinApp(string appId, string storageFolder)
    {
        VhLogger.Instance = VhLogger.CreateConsoleLogger();
        _appId = appId;
        _storageFolder = storageFolder;
        _appIcon = Icon.ExtractAssociatedIcon(Assembly.GetEntryAssembly()?.Location ?? throw new Exception("Could not get the location of Assembly."))
                   ?? throw new Exception("Could not get the icon of the executing assembly.");

        //create command Listener
        _commandListener = new CommandListener(Path.Combine(_storageFolder, FileNameAppCommand));
        _commandListener.CommandReceived += CommandListener_CommandReceived;
    }

    public static VpnHoodWinApp Init(string appId, string storageFolder)
    {
        return new VpnHoodWinApp(appId, storageFolder);
    }

    public static void SetWindowTitleBarColor(IntPtr hWnd, Color color)
    {
        var attrValue = new[] { color.B << 16 | color.G << 8 | color.R };
        const int captionColor = 35;
        DwmSetWindowAttribute(hWnd, captionColor, attrValue, attrValue.Length * 4);
    }

    public static void OpenUrlInExternalBrowser(Uri url)
    {
        Process.Start(new ProcessStartInfo {
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

    public bool IsAnotherInstanceRunning()
    {
        _instanceMutex ??= new Mutex(false, _appId);

        // Make single instance
        // if you like to wait a few seconds in case that the instance is just shutting down
        return !_instanceMutex.WaitOne(TimeSpan.FromSeconds(0), false);
    }

    public void PreStart(string[] args)
    {
        ConnectAfterStart = args.Any(x => x.Equals("/autoconnect", StringComparison.OrdinalIgnoreCase));
        ShowWindowAfterStart = !ConnectAfterStart &&
                               !args.Any(x => x.Equals("/nowindow", StringComparison.OrdinalIgnoreCase));

        // Make single instance
        // if you like to wait a few seconds in case that the instance is just shutting down
        if (IsAnotherInstanceRunning()) {
            // open main window if app is already running and user run the app again
            if (ShowWindowAfterStart)
                _commandListener.SendCommand("/openWindow");
            throw new Exception("VpnHood client is already running.");
        }

        // configuring Windows Firewall
        try {
            OpenLocalFirewall(_storageFolder);
        }
        catch {
            /*ignored*/
        }
    }

    public bool Start()
    {
        // auto connect
        if (ConnectAfterStart && VpnHoodApp.Instance.CurrentClientProfileInfo != null)
            _ = VpnHoodApp.Instance.Connect(VpnHoodApp.Instance.CurrentClientProfileInfo.ClientProfileId);

        // create notification icon
        InitNotifyIcon();
        VpnHoodApp.Instance.ConnectionStateChanged += (_, _) => UpdateNotifyIcon();
        UpdateNotifyIcon();

        // start command listener
        _commandListener.Start();
        return true;
    }

    private void InitNotifyIcon()
    {
        _sysTray = new SystemTray(VpnHoodApp.Instance.Resource.Strings.AppName, _appIcon.Handle);
        _sysTray.Clicked += (_, _) => OpenMainWindow();
        _sysTray.ContextMenu = new ContextMenu();
        _openMainWindowMenuItemId =
            _sysTray.ContextMenu.AddMenuItem(VpnHoodApp.Instance.Resource.Strings.Open, (_, _) => OpenMainWindow());
        _openMainWindowInBrowserMenuItemId =
            _sysTray.ContextMenu.AddMenuItem(VpnHoodApp.Instance.Resource.Strings.OpenInBrowser,
                (_, _) => OpenMainWindowInBrowser());
        _sysTray.ContextMenu.AddMenuSeparator();
        _connectMenuItemId =
            _sysTray.ContextMenu.AddMenuItem(VpnHoodApp.Instance.Resource.Strings.Connect, (_, _) => ConnectClicked());
        _disconnectMenuItemId = _sysTray.ContextMenu.AddMenuItem(VpnHoodApp.Instance.Resource.Strings.Disconnect,
            (_, _) => _ = VpnHoodApp.Instance.Disconnect(true));
        _sysTray.ContextMenu.AddMenuSeparator();
        _sysTray.ContextMenu.AddMenuItem(VpnHoodApp.Instance.Resource.Strings.Exit, (_, _) => Exit());

        // initialize icons
        if (VpnHoodApp.Instance.Resource.Icons.SystemTrayConnectingIcon != null)
            _connectingIcon =
                new Icon(new MemoryStream(VpnHoodApp.Instance.Resource.Icons.SystemTrayConnectingIcon.Data));

        if (VpnHoodApp.Instance.Resource.Icons.SystemTrayConnectedIcon != null)
            _connectedIcon =
                new Icon(new MemoryStream(VpnHoodApp.Instance.Resource.Icons.SystemTrayConnectedIcon.Data));

        if (VpnHoodApp.Instance.Resource.Icons.SystemTrayDisconnectedIcon != null)
            _disconnectedIcon =
                new Icon(new MemoryStream(VpnHoodApp.Instance.Resource.Icons.SystemTrayDisconnectedIcon.Data));
    }

    private void UpdateNotifyIcon()
    {
        if (!VpnHoodApp.IsInit || _sysTray == null)
            return;

        // update icon and text
        var stateName = VpnHoodApp.Instance.State.ConnectionState == AppConnectionState.None
            ? VpnHoodApp.Instance.Resource.Strings.Disconnected
            : VpnHoodApp.Instance.State.ConnectionState.ToString();

        var icon = _connectingIcon;
        if (VpnHoodApp.Instance.State.ConnectionState == AppConnectionState.Connected) icon = _connectedIcon;
        else if (VpnHoodApp.Instance.IsIdle) icon = _disconnectedIcon;
        icon ??= _appIcon;

        _sysTray.Update($@"{VpnHoodApp.Instance.Resource.Strings.AppName} - {stateName}", icon.Handle);
        _sysTray.ContextMenu?.EnableMenuItem(_connectMenuItemId, VpnHoodApp.Instance.IsIdle);
        _sysTray.ContextMenu?.EnableMenuItem(_connectMenuItemId, VpnHoodApp.Instance.IsIdle);
        _sysTray.ContextMenu?.EnableMenuItem(_disconnectMenuItemId,
            !VpnHoodApp.Instance.IsIdle &&
            VpnHoodApp.Instance.State.ConnectionState != AppConnectionState.Disconnecting);
        _sysTray.ContextMenu?.EnableMenuItem(_openMainWindowMenuItemId, EnableOpenMainWindow);
        _sysTray.ContextMenu?.EnableMenuItem(_openMainWindowInBrowserMenuItemId, true);
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
        if (VpnHoodApp.Instance.Settings.UserSettings.ClientProfileId != null) {
            try {
                _ = VpnHoodApp.Instance.Connect(VpnHoodApp.Instance.Settings.UserSettings.ClientProfileId.Value);
            }
            catch {
                OpenMainWindow();
            }
        }
        else {
            OpenMainWindow();
        }
    }

    private static string FindExePath(string exe)
    {
        exe = Environment.ExpandEnvironmentVariables(exe);
        if (File.Exists(exe))
            return Path.GetFullPath(exe);

        if (Path.GetDirectoryName(exe) == string.Empty)
            foreach (var test in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')) {
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
        ProcessStartNoWindow("netsh",
                $"advfirewall firewall add rule  name=\"{ruleName}\" program=\"{exePath}\" protocol=TCP localport=any action=allow profile=any dir=in")
            .WaitForExit();
        ProcessStartNoWindow("netsh",
                $"advfirewall firewall add rule  name=\"{ruleName}\" program=\"{exePath}\" protocol=UDP localport=any action=allow profile=any dir=in")
            .WaitForExit();

        // VpnHood exe
        exePath = curExePath;
        if (File.Exists(exePath)) {
            ProcessStartNoWindow("netsh",
                    $"advfirewall firewall add rule  name=\"{ruleName}\" program=\"{exePath}\" protocol=TCP localport=any action=allow profile=any dir=in")
                .WaitForExit();
            ProcessStartNoWindow("netsh",
                    $"advfirewall firewall add rule  name=\"{ruleName}\" program=\"{exePath}\" protocol=UDP localport=any action=allow profile=any dir=in")
                .WaitForExit();
        }

        // save firewall modified
        File.WriteAllText(lastFirewallConfig, curExePath);
    }

    private static Process ProcessStartNoWindow(string filename, string argument)
    {
        var processStart = new ProcessStartInfo(filename, argument) {
            CreateNoWindow = true
        };

        return Process.Start(processStart) ?? throw new Exception($"Could not start process: {filename}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _sysTray?.Dispose();
        DisposeSingleton();
    }

    public static Uri? RegisterLocalDomain(IPEndPoint hostEndPoint, string localHost)
    {
        // check default ip
        IPEndPoint? freeLocalEndPoint = null;
        try {
            freeLocalEndPoint = VhUtil.GetFreeTcpEndPoint(hostEndPoint.Address, hostEndPoint.Port);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError("Could not find free port local host. LocalIp:{LocalIp}, Message: {Message}",
                hostEndPoint.Address, ex.Message);
        }

        // check 127.0.0.1
        if (freeLocalEndPoint == null) {
            try {
                freeLocalEndPoint = VhUtil.GetFreeTcpEndPoint(IPAddress.Loopback, 9090);
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError("Could not find free port local host. LocalIp:{LocalIp}, Message: {Message}",
                    IPAddress.Loopback, ex.Message);
                return null;
            }
        }

        try {
            var hostsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers",
                "etc", "hosts");
            var hostLines = File.ReadLines(hostsFilePath).ToList();

            // remove wrong items
            var newHostLines = hostLines
                .Where(x => {
                    var items = x.Split(" ");
                    var isWrongVpnHoodLine =
                        items.Length > 1 &&
                        items[0].Trim() != freeLocalEndPoint.Address.ToString() &&
                        items[1].Trim().Equals(localHost, StringComparison.OrdinalIgnoreCase);
                    return !isWrongVpnHoodLine;
                })
                .ToList();

            // add item if it does not exist
            var isAlreadyAdded = newHostLines
                .Any(x => {
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
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not register local domain.");
            return null;
        }
    }
}
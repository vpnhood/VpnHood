using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.App.Win.WinNative;
using VpnHood.Core.Client.Devices.Win;
using VpnHood.Core.Common;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Graphics;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppLib.App.Win;

public class VpnHoodAppWin : Singleton<VpnHoodAppWin>, IDisposable
{
    private readonly string _appId;
    private const string FileNameAppCommand = "appcommand";
    private Mutex? _instanceMutex;
    private SystemTray? _sysTray;
    private readonly CommandListener _commandListener;
    private readonly IntPtr _appIcon;
    private IntPtr _disconnectedIcon;
    private IntPtr _connectedIcon;
    private IntPtr _connectingIcon;
    private int _connectMenuItemId;
    private int _disconnectMenuItemId;
    private int _openMainWindowMenuItemId;

    public event EventHandler? OpenMainWindowRequested;
    public event EventHandler? ExitRequested;
    public bool ShowWindowAfterStart { get; private set; }
    public bool ConnectAfterStart { get; private set; }

    [DllImport("DwmApi")]
    private static extern int DwmSetWindowAttribute(IntPtr hWnd, int attr, int[] attrValue, int attrSize);

    private VpnHoodAppWin(string appId, string storageFolder)
    {
        VhLogger.Instance = new VhConsoleLogger();
        _appId = appId;

        // get app icon from executable
        var assemblyLocation = Assembly.GetEntryAssembly()?.Location ??
                               throw new Exception("Could not get the location of Assembly.");
        _appIcon = WinIcon.ExtractLargeIcon(assemblyLocation);
        if (_appIcon == IntPtr.Zero)
            throw new Exception("Could not get the icon of the executing assembly.");

        //create command Listener
        _commandListener = new CommandListener(Path.Combine(storageFolder, FileNameAppCommand));
        _commandListener.CommandReceived += CommandListener_CommandReceived;
    }

    // The app on Windows, before any UI: the single instance, the device, the web server a paired
    // device dials, the tray. Which UI shows it, and how it draws one, is the head's next step - so
    // nothing here belongs to a UI framework. Throws when another instance is running, after asking
    // it for its window.
    public static VpnHoodAppWin Init(Func<AppOptions> optionsFactory, string[] args)
    {
        var appOptions = optionsFactory();
        appOptions.DeviceId ??= WindowsIdentity.GetCurrent().User?.Value;
        appOptions.DeviceUiProvider = new WinDeviceUiProvider();
        appOptions.EventWatcherInterval ??= TimeSpan.FromSeconds(1);

        var appWin = Init(appOptions, args);
        appWin.Start();
        return appWin;
    }

    public static VpnHoodAppWin Init(AppOptions appOptions, string[] args)
    {
        // create app
        var ret = new VpnHoodAppWin(appOptions.AppId, appOptions.StorageFolderPath);
        ret.PreStart(args);

        // initialize VpnHoodApp
        var device = new WinDevice(appOptions.StorageFolderPath, appOptions.IsDebugMode);
        VpnHoodApp.Init(device, appOptions);
        return ret;
    }

    public static void SetWindowTitleBarColor(IntPtr hWnd, VhColor color)
    {
        var attrValue = new[] { (color.B << 16) | (color.G << 8) | color.R };
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

    private void PreStart(string[] args)
    {
        ConnectAfterStart = args.Any(x => x.Equals("/autoconnect", StringComparison.OrdinalIgnoreCase));
        ShowWindowAfterStart = !ConnectAfterStart &&
                               !args.Any(x => x.Equals("/nowindow", StringComparison.OrdinalIgnoreCase));

        // Make single instance
        // if you like to wait a few seconds in case that the instance is just shutting down
        if (IsAnotherInstanceRunning()) {
            // open main window if app is already running and user run the app again
            if (ShowWindowAfterStart)
                _commandListener.TrySendCommand("/openWindow");
            throw new Exception("VpnHood client is already running.");
        }
    }

    public bool Start()
    {
        // auto connect
        if (ConnectAfterStart && VpnHoodApp.Instance.CurrentClientProfileInfo != null)
            _ = VpnHoodApp.Instance.TryConnect();

        // The tray comes up once the look it draws with is final - there is nothing to see before
        // it, where an icon that changed under the user would be seen.
        _ = InitNotifyIconWhenReady();
        VpnHoodApp.Instance.ConnectionStateChanged += (_, _) => UpdateNotifyIcon();

        // start command listener
        _commandListener.Start();
        return true;
    }

    private async Task InitNotifyIconWhenReady()
    {
        try {
            // no ConfigureAwait(false): a tray icon is a window, and a window belongs to the thread
            // that made it - this must come back to the one that started the app
            await VpnHoodApp.Instance.ResourcesLoaded;
            InitNotifyIcon();
            UpdateNotifyIcon();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not create the system tray icon.");
        }
    }

    private void InitNotifyIcon()
    {
        _sysTray = new SystemTray(VpnHoodApp.Instance.Features.AppName, _appIcon);
        _sysTray.Clicked += (_, _) => OpenMainWindow();
        _sysTray.ContextMenu = new ContextMenu();
        _openMainWindowMenuItemId =
            _sysTray.ContextMenu.AddMenuItem(VpnHoodApp.Instance.Resources.Strings.Open, (_, _) => OpenMainWindow());
        _sysTray.ContextMenu.AddMenuSeparator();
        _connectMenuItemId =
            _sysTray.ContextMenu.AddMenuItem(VpnHoodApp.Instance.Resources.Strings.Connect,
                (_, _) => _ = ConnectClicked());
        _disconnectMenuItemId = _sysTray.ContextMenu.AddMenuItem(VpnHoodApp.Instance.Resources.Strings.Disconnect,
            (_, _) => _ = VpnHoodApp.Instance.TryDisconnect());
        _sysTray.ContextMenu.AddMenuSeparator();
        _sysTray.ContextMenu.AddMenuItem(VpnHoodApp.Instance.Resources.Strings.Exit, (_, _) => Exit());

        // initialize icons from icon data
        if (VpnHoodApp.Instance.Resources.Icons.SystemTrayConnectingIconData != null)
            _connectingIcon =
                WinIcon.LoadIconFromBytes(VpnHoodApp.Instance.Resources.Icons.SystemTrayConnectingIconData.Value.Span);

        if (VpnHoodApp.Instance.Resources.Icons.SystemTrayConnectedIconData != null)
            _connectedIcon =
                WinIcon.LoadIconFromBytes(VpnHoodApp.Instance.Resources.Icons.SystemTrayConnectedIconData.Value.Span);

        if (VpnHoodApp.Instance.Resources.Icons.SystemTrayDisconnectedIconData != null)
            _disconnectedIcon =
                WinIcon.LoadIconFromBytes(VpnHoodApp.Instance.Resources.Icons.SystemTrayDisconnectedIconData.Value.Span);
    }

    private void UpdateNotifyIcon()
    {
        if (!VpnHoodApp.IsInit || _sysTray == null)
            return;

        // update icon and text
        var stateName = VpnHoodApp.Instance.State.ConnectionState == AppConnectionState.None
            ? VpnHoodApp.Instance.Resources.Strings.Disconnected
            : VpnHoodApp.Instance.State.ConnectionState.ToString();

        var icon = _connectingIcon;
        if (VpnHoodApp.Instance.State.ConnectionState == AppConnectionState.Connected) icon = _connectedIcon;
        else if (VpnHoodApp.Instance.IsIdle) icon = _disconnectedIcon;
        if (icon == IntPtr.Zero) icon = _appIcon;

        _sysTray.Update($@"{VpnHoodApp.Instance.Features.AppName} - {stateName}", icon);
        _sysTray.ContextMenu?.EnableMenuItem(_connectMenuItemId, VpnHoodApp.Instance.IsIdle);
        _sysTray.ContextMenu?.EnableMenuItem(_connectMenuItemId, VpnHoodApp.Instance.IsIdle);
        _sysTray.ContextMenu?.EnableMenuItem(_disconnectMenuItemId,
            !VpnHoodApp.Instance.IsIdle &&
            VpnHoodApp.Instance.State.ConnectionState != AppConnectionState.Disconnecting);
    }

    // What a head shows when the tray asks for the window is the head's own business - its window,
    // or whatever stands in for one. This only asks.
    private void OpenMainWindow()
    {
        OpenMainWindowRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Exit()
    {
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    private async Task ConnectClicked()
    {
        // open main window if no profile is selected
        if (VpnHoodApp.Instance.UserSettings.ClientProfileId == null) {
            OpenMainWindow();
            return;
        }

        // connect
        try {
            await VpnHoodApp.Instance.Connect().Vhc();
        }
        catch {
            OpenMainWindow();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            _commandListener.Dispose();
            _instanceMutex?.Dispose();
            _sysTray?.Dispose();

            // disconnect and dispose app
            if (VpnHoodApp.IsInit)
                VpnHoodApp.Instance.Dispose();
        }

        // Clean up icon handles
        if (_appIcon != IntPtr.Zero) WinIcon.DestroyIcon(_appIcon);
        if (_disconnectedIcon != IntPtr.Zero) WinIcon.DestroyIcon(_disconnectedIcon);
        if (_connectedIcon != IntPtr.Zero) WinIcon.DestroyIcon(_connectedIcon);
        if (_connectingIcon != IntPtr.Zero) WinIcon.DestroyIcon(_connectingIcon);
    }
}
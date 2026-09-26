using System.Reflection;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Api;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.App.Branding;
using VpnHood.AppLib.App.Windows.WinNative;
using VpnHood.Net.Toolkit.Assets;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppLib.App.Windows;

// The app's icon in the notification area, over its API: it shows and drives the same app the window
// does, whichever process that app runs in - the service's, over loopback, or one in this process -
// and it is started beside any UI, since it keeps a thread of its own with the message loop its
// hidden windows need. It follows the connection by asking the app each second. Its words are this
// library's (AppResources); its pictures are the UI store's branding, under the theme the app names.
//
// What it cannot do itself it asks of whoever started it: bring the window forward, and end - Exit
// while connected disconnects first ("Disconnect and exit").
public sealed class WindowsAppTray : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private readonly VpnHoodApi _api;
    private readonly IAssetProvider? _uiAssets;
    private readonly Func<CancellationToken, Task> _showWindow;
    private readonly Action _exit;
    private readonly AppResources _resources = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly MessageLoopThread _thread = new("VpnHood tray");
    private readonly Task _polling;
    private volatile AppState? _state;

    // on the tray's thread only
    private SystemTray? _systemTray;
    private string _appName = "";
    private IntPtr _appIcon;
    private IntPtr _connectedIcon;
    private IntPtr _connectingIcon;
    private IntPtr _disconnectedIcon;
    private int _connectMenuItemId;
    private int _disconnectMenuItemId;

    private WindowsAppTray(VpnHoodApi api, IAssetProvider? uiAssets, Func<CancellationToken, Task> showWindow,
        Action exit)
    {
        _api = api;
        _uiAssets = uiAssets;
        _showWindow = showWindow;
        _exit = exit;
        _polling = Task.Run(Run);
    }

    // showWindow: the window to the front, however the UI does it. exit: the UI ends; the app it
    // shows goes on only where it runs in a service.
    public static WindowsAppTray Start(VpnHoodApi api, IAssetProvider? uiAssets,
        Func<CancellationToken, Task> showWindow, Action exit)
    {
        return new WindowsAppTray(api, uiAssets, showWindow, exit);
    }

    private async Task Run()
    {
        var cancellationToken = _cancellation.Token;
        try {
            // what it draws with, once, before it is drawn: a tray icon that changes under the person
            // is seen, one that appears a moment later is not
            var info = await GetInfo(cancellationToken).Vhc();
            if (_uiAssets != null)
                await AppBranding.LoadAsync(_resources, _uiAssets, info.Features.UiTheme).Vhc();

            _thread.Post(() => Create(info.Features.AppName));

            AppState? drawn = null;
            while (true) {
                try {
                    var state = await _api.App.GetState(cancellationToken).Vhc();
                    _state = state;
                    if (drawn == null || drawn.ConnectionState != state.ConnectionState ||
                        drawn.CanConnect != state.CanConnect || drawn.CanDisconnect != state.CanDisconnect) {
                        drawn = state;
                        _thread.Post(() => Update(state));
                    }
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested) {
                    VhLogger.Instance.LogDebug(ex, "The tray could not read the app's state.");
                }

                await Task.Delay(PollInterval, cancellationToken).Vhc();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // the tray is going
        }
    }

    // The app may be starting - a service that restarted - so this asks until it is answered.
    private async Task<AppInfo> GetInfo(CancellationToken cancellationToken)
    {
        while (true) {
            try {
                return await _api.App.GetInfo(cancellationToken).Vhc();
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested) {
                VhLogger.Instance.LogDebug(ex, "The tray could not reach the app yet. Retrying...");
                await Task.Delay(PollInterval, cancellationToken).Vhc();
            }
        }
    }

    private void Create(string appName)
    {
        _appName = appName;
        _appIcon = LoadAppIcon();
        _connectingIcon = TryLoadIcon(_resources.Icons.SystemTrayConnectingIconData);
        _connectedIcon = TryLoadIcon(_resources.Icons.SystemTrayConnectedIconData);
        _disconnectedIcon = TryLoadIcon(_resources.Icons.SystemTrayDisconnectedIconData);

        var strings = _resources.Strings;
        _systemTray = new SystemTray(appName, _disconnectedIcon != IntPtr.Zero ? _disconnectedIcon : _appIcon);
        _systemTray.Clicked += (_, _) => _ = ShowWindow();
        _systemTray.ContextMenu = new ContextMenu();
        _systemTray.ContextMenu.AddMenuItem(strings.Open, (_, _) => _ = ShowWindow());
        _systemTray.ContextMenu.AddMenuSeparator();
        _connectMenuItemId = _systemTray.ContextMenu.AddMenuItem(strings.Connect, (_, _) => _ = Connect());
        _disconnectMenuItemId = _systemTray.ContextMenu.AddMenuItem(strings.Disconnect, (_, _) => _ = Disconnect());
        _systemTray.ContextMenu.AddMenuSeparator();
        _systemTray.ContextMenu.AddMenuItem(strings.Exit, (_, _) => _ = Exit());

        if (_state is { } state)
            Update(state);
    }

    private void Update(AppState state)
    {
        if (_systemTray == null)
            return;

        var stateName = state.ConnectionState == AppConnectionState.None
            ? _resources.Strings.Disconnected
            : state.ConnectionState.ToString();

        var icon = state.ConnectionState == AppConnectionState.Connected ? _connectedIcon
            : state.IsIdle ? _disconnectedIcon
            : _connectingIcon;

        _systemTray.Update($"{_appName} - {stateName}", icon != IntPtr.Zero ? icon : _appIcon);
        _systemTray.ContextMenu?.EnableMenuItem(_connectMenuItemId, state.CanConnect);
        _systemTray.ContextMenu?.EnableMenuItem(_disconnectMenuItemId, state.CanDisconnect);
    }

    private async Task ShowWindow()
    {
        try {
            await _showWindow(_cancellation.Token).Vhc();
        }
        catch (Exception ex) when (!_cancellation.IsCancellationRequested) {
            VhLogger.Instance.LogWarning(ex, "The tray could not bring the window forward.");
        }
    }

    // With no profile to connect to, or a connect that fails, the window is where the person
    // can do something about it.
    private async Task Connect()
    {
        if (_state?.ClientProfile == null) {
            await ShowWindow().Vhc();
            return;
        }

        try {
            await _api.App.Connect(null, null, ConnectPlanId.Normal, _cancellation.Token).Vhc();
        }
        catch (Exception ex) when (!_cancellation.IsCancellationRequested) {
            VhLogger.Instance.LogWarning(ex, "The tray's connect failed.");
            await ShowWindow().Vhc();
        }
    }

    private async Task Disconnect()
    {
        try {
            await _api.App.Disconnect(_cancellation.Token).Vhc();
        }
        catch (Exception ex) when (!_cancellation.IsCancellationRequested) {
            VhLogger.Instance.LogWarning(ex, "The tray's disconnect failed.");
        }
    }

    private async Task Exit()
    {
        if (_state is { IsIdle: false })
            await Disconnect().Vhc();

        _exit();
    }

    // The executable's own icon, which every Windows head carries (ApplicationIcon): the entry
    // assembly holds it as a resource whether it was started as its apphost or through dotnet.
    private static IntPtr LoadAppIcon()
    {
        foreach (var path in new[] { Assembly.GetEntryAssembly()?.Location, Environment.ProcessPath }) {
            if (string.IsNullOrEmpty(path))
                continue;

            try {
                return WinIcon.ExtractLargeIcon(path);
            }
            catch (InvalidOperationException) {
                // no icon in this one; the next
            }
        }

        return IntPtr.Zero;
    }

    private static IntPtr TryLoadIcon(ReadOnlyMemory<byte>? iconData)
    {
        if (iconData == null)
            return IntPtr.Zero;

        try {
            return WinIcon.LoadIconFromBytes(iconData.Value.Span);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "The tray could not load one of its icons.");
            return IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        try {
            _polling.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException ex) {
            VhLogger.Instance.LogError(ex, "The tray's polling ended with an error.");
        }

        _thread.Post(Destroy);
        _thread.Dispose();
        _cancellation.Dispose();
    }

    private void Destroy()
    {
        _systemTray?.ContextMenu?.Dispose();
        _systemTray?.Dispose();
        _systemTray = null;

        foreach (var icon in new[] { _appIcon, _connectedIcon, _connectingIcon, _disconnectedIcon })
            if (icon != IntPtr.Zero)
                WinIcon.DestroyIcon(icon);
    }
}

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Api;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.App;
using VpnHood.AppUi.Common;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Avalonia.Desktop;

// The app's icon in the tray, beside the window: the connection at a glance - its state's icon, and
// in words when pointed at - and a menu that opens the window, connects, disconnects and ends the UI.
// It follows the connection by asking the app each second over the window's API, so it shows the
// service's app and the one in this process alike. Its words are the UI's, in the language the UI
// shows; its pictures the UI store's branding. On the UI thread throughout.
//
// What it cannot do itself it asks of the window's host: bring the window forward, and end - Exit
// while connected disconnects first ("Disconnect and exit"). The service runs on.
internal sealed class AvaloniaDesktopTray : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private readonly VpnHoodApi _api;
    private readonly string _appName;
    private readonly Action _showWindow;
    private readonly Action _exit;
    private readonly TrayIcon _trayIcon;
    private readonly WindowIcon _connectedIcon;
    private readonly WindowIcon _connectingIcon;
    private readonly WindowIcon _disconnectedIcon;
    private readonly NativeMenuItem _openItem;
    private readonly NativeMenuItem _connectItem;
    private readonly NativeMenuItem _disconnectItem;
    private readonly NativeMenuItem _exitItem;
    private readonly CancellationTokenSource _cancellation = new();
    private AppState? _state;

    public AvaloniaDesktopTray(Application application, VpnHoodApi api, string appName, AppResources.AppIcons icons,
        Action showWindow, Action exit)
    {
        _api = api;
        _appName = appName;
        _showWindow = showWindow;
        _exit = exit;
        var builtInIcons = new AppResources.AppIcons();
        _connectedIcon = LoadIcon(icons.SystemTrayConnectedIconData, builtInIcons.SystemTrayConnectedIconData);
        _connectingIcon = LoadIcon(icons.SystemTrayConnectingIconData, builtInIcons.SystemTrayConnectingIconData);
        _disconnectedIcon = LoadIcon(icons.SystemTrayDisconnectedIconData, builtInIcons.SystemTrayDisconnectedIconData);

        _openItem = CreateItem(ShowWindow);
        _connectItem = CreateItem(() => _ = Connect());
        _disconnectItem = CreateItem(() => _ = Disconnect());
        _exitItem = CreateItem(() => _ = Exit());
        _trayIcon = new TrayIcon {
            Icon = _disconnectedIcon,
            ToolTipText = appName,
            Menu = new NativeMenu {
                Items = {
                    _openItem, new NativeMenuItemSeparator(),
                    _connectItem, _disconnectItem, new NativeMenuItemSeparator(),
                    _exitItem
                }
            }
        };
        _trayIcon.Clicked += (_, _) => ShowWindow();
        UpdateWords();
        Strings.Current.PropertyChanged += Strings_PropertyChanged;

        // attached to the application, which removes it from the tray when the UI thread ends; the
        // state followed once the UI thread's loop runs, where its awaits come back to
        TrayIcon.SetIcons(application, new TrayIcons { _trayIcon });
        var cancellationToken = _cancellation.Token;
        Dispatcher.UIThread.Post(() => _ = Follow(cancellationToken));
    }

    private static NativeMenuItem CreateItem(Action click)
    {
        var item = new NativeMenuItem();
        item.Click += (_, _) => click();
        return item;
    }

    // The tray's pictures are icon files, which Avalonia reads on Windows and Linux alike. A branded
    // one that cannot be read is logged, and the library's own takes its place rather than an empty
    // icon no one could find.
    private static WindowIcon LoadIcon(ReadOnlyMemory<byte>? iconData, ReadOnlyMemory<byte>? builtInIconData)
    {
        try {
            return ReadIcon(iconData);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "The tray could not read one of its icons, so it shows the built-in one.");
            return ReadIcon(builtInIconData);
        }
    }

    private static WindowIcon ReadIcon(ReadOnlyMemory<byte>? iconData)
    {
        var data = iconData ?? throw new ArgumentNullException(nameof(iconData));
        return new WindowIcon(new MemoryStream(data.ToArray()));
    }

    // A language chosen while the UI runs: on the UI thread, where the words are read.
    private void Strings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(UpdateWords);
    }

    private void UpdateWords()
    {
        if (_cancellation.IsCancellationRequested)
            return;

        _openItem.Header = Strings.Current.Open;
        _connectItem.Header = Strings.Current.Connect;
        _disconnectItem.Header = Strings.Current.Disconnect;
        _exitItem.Header = Strings.Current.Exit;
        if (_state != null)
            Update(_state);
    }

    // The app's state each second; one that cannot be read - a service restarting - is asked again.
    private async Task Follow(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            try {
                var state = await _api.App.GetState(cancellationToken);
                if (!cancellationToken.IsCancellationRequested && IsChanged(_state, state))
                    Update(state);
                _state = state;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                return;
            }
            catch (Exception ex) {
                VhLogger.Instance.LogDebug(ex, "The tray could not read the app's state.");
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing |
                                                                             ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
    }

    private static bool IsChanged(AppState? drawn, AppState state)
    {
        return drawn == null || drawn.ConnectionState != state.ConnectionState ||
               drawn.CanConnect != state.CanConnect || drawn.CanDisconnect != state.CanDisconnect;
    }

    private void Update(AppState state)
    {
        _trayIcon.Icon = state.ConnectionState == AppConnectionState.Connected ? _connectedIcon
            : state.IsIdle ? _disconnectedIcon
            : _connectingIcon;
        _trayIcon.ToolTipText = $"{_appName} - {Strings.Current.ConnectionStateText(state.ConnectionState)}";
        _connectItem.IsEnabled = state.CanConnect;
        _disconnectItem.IsEnabled = state.CanDisconnect;
    }

    private void ShowWindow()
    {
        _showWindow();
    }

    // With no profile to connect to, or a connect that fails, the window is where the person can do
    // something about it.
    private async Task Connect()
    {
        if (_state?.VpnProfile == null) {
            ShowWindow();
            return;
        }

        try {
            await _api.App.Connect(null, null, ConnectPlanId.Normal, _cancellation.Token);
        }
        catch (Exception ex) when (!_cancellation.IsCancellationRequested) {
            VhLogger.Instance.LogWarning(ex, "The tray's connect failed.");
            ShowWindow();
        }
    }

    private async Task Disconnect()
    {
        try {
            await _api.App.Disconnect(_cancellation.Token);
        }
        catch (Exception ex) when (!_cancellation.IsCancellationRequested) {
            VhLogger.Instance.LogWarning(ex, "The tray's disconnect failed.");
        }
    }

    private async Task Exit()
    {
        if (_state is { IsIdle: false })
            await Disconnect();

        _exit();
    }

    public void Dispose()
    {
        if (_cancellation.IsCancellationRequested)
            return;

        _cancellation.Cancel();
        Strings.Current.PropertyChanged -= Strings_PropertyChanged;
        _trayIcon.Dispose();
        _cancellation.Dispose();
    }
}

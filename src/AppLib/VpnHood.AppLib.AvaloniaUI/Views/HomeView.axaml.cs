using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using VpnHood.AppLib.AvaloniaUI.Helpers;
using VpnHood.AppLib.AvaloniaUI.ViewModels;

namespace VpnHood.AppLib.AvaloniaUI.Views;

public partial class HomeView : UserControl, IPage
{
    // the web UI's openDebugDialog: five clicks, and a counter that forgets after five seconds
    private static readonly TimeSpan DebugTapWindow = TimeSpan.FromSeconds(5);
    private const int DebugTapCount = 5;

    private readonly MainViewModel _viewModel;
    private readonly MainView _host;
    private long _firstDebugTap;
    private int _debugTaps;

    public HomeView(MainViewModel viewModel, MainView host)
    {
        _viewModel = viewModel;
        _host = host;
        DataContext = viewModel;
        InitializeComponent();
    }

    // Connect is where the input starts, as on the web UI's home.
    public void FocusDefault()
    {
        ConnectButton.LandFocus();
    }

    private async void OnConnectClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.ToggleConnect();
    }

    // The web UI's ServersButton: the page opens unless there is nothing behind it to choose, and
    // then the row says why rather than opening an empty page.
    private void OnServersClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.ServersUnreachableReason() is { } reason) {
            _viewModel.ShowNotice(reason);
            return;
        }

        _host.Navigate(new LocationsView(_viewModel, _host));
    }

    private void OnPhoneClick(object? sender, RoutedEventArgs e)
    {
        _host.Navigate(new PairingView(_host));
    }

    // The developer page, opened as the web UI opens its dialog (HomePageHeader.vue): the fifth
    // tap of a burst, or the first when a debug field is already set.
    private void OnVersionClick(object? sender, RoutedEventArgs e)
    {
        if (_debugTaps == 0 || Stopwatch.GetElapsedTime(_firstDebugTap) > DebugTapWindow) {
            _firstDebugTap = Stopwatch.GetTimestamp();
            _debugTaps = 0;
        }

        _debugTaps++;
        if (!_viewModel.HasDebugData && _debugTaps < DebugTapCount)
            return;

        _debugTaps = 0;
        _host.Navigate(new DeveloperView(_host));
    }
}

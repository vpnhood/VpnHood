using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using VpnHood.AppLib.Assets;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Helpers;
using VpnHood.AppUi.Presentation.Classic.Avalonia.ViewModels;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Views.Dialogs;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Views;

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
        viewModel.PropertyChanged += (_, e) => {
            if (e.PropertyName is nameof(MainViewModel.CountdownKind) or "")
                UpdateCountdownColor();
        };
        UpdateCountdownColor();
    }

    // the countdown's colour steps down as the time does (CountDown.getCountdownColor)
    private void UpdateCountdownColor()
    {
        CountdownChip.Classes.Set("normal", _viewModel.CountdownKind == "normal");
        CountdownChip.Classes.Set("alert", _viewModel.CountdownKind == "alert");
        CountdownChip.Classes.Set("warning-time", _viewModel.CountdownKind == "warning");
    }

    // Connect is where the input starts, as on the web UI's home.
    public void FocusDefault()
    {
        ConnectButton.LandFocus();
    }

    private async void OnConnectClick(object? sender, RoutedEventArgs e)
    {
        try {
            await _viewModel.ToggleConnect();
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
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

    private void OnSplitCountriesClick(object? sender, RoutedEventArgs e)
    {
        _host.Navigate(new SplitCountriesView(_host));
    }

    private void OnSplitAppsClick(object? sender, RoutedEventArgs e)
    {
        _host.Navigate(new SplitAppsView(_host));
    }

    private void OnProtocolClick(object? sender, RoutedEventArgs e)
    {
        _host.Navigate(new ProtocolsView(_host));
    }

    private void OnPhoneClick(object? sender, RoutedEventArgs e)
    {
        _host.Navigate(new PairingView(_host));
    }

    private void OnMenuClick(object? sender, RoutedEventArgs e)
    {
        _host.OpenDrawer();
    }

    private void OnStatisticsClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.IsConnected)
            _host.Navigate(new StatisticsView(_host));
    }

    private void OnGoPremiumClick(object? sender, RoutedEventArgs e)
    {
        _host.Navigate(new PurchaseSubscriptionView(_host, null));
    }

    private void OnExtendClick(object? sender, RoutedEventArgs e)
    {
        _host.Navigate(new ExtendSessionView(_host));
    }

    // The account: signed in, its page; signed out, the sign-in - the chooser where there is one,
    // the phone where the only way is the email form a remote cannot fill (index.vue's
    // onAccountClick).
    private async void OnAccountClick(object? sender, RoutedEventArgs e)
    {
        try {
            if (AppModel.Account != null) {
                _host.Navigate(new AccountView(_host));
                return;
            }

            if (AppModel.PrimaryProviderId == null) {
                _host.Navigate(new PairingView(_host, Strings.Current.RemoteAccessHintSignIn));
                return;
            }

            if (AppModel.HasSignInChoice) {
                await _host.ShowDialog(new SignInDialog(_host));
                return;
            }

            try {
                await _viewModel.SignIn();
            }
            catch (Exception ex) {
                await _host.ProcessError(ex);
            }
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnBadgeClick(object? sender, RoutedEventArgs e)
    {
        try {
            await _host.ShowDialog(new BadgeDialog(_host, _viewModel.Badges));
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
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

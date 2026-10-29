using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using VpnHood.AppUi.Common;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Helpers;
using VpnHood.AppUi.Presentation.Classic.Avalonia.ViewModels;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Views.Dialogs;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Views;

public partial class HomeView : UserControl, IPage
{
    // five taps, each within two seconds of the one before: an unhurried finger gets there, and a
    // pause starts over. A limit from the first tap made the fifth a race.
    private static readonly TimeSpan DebugTapGap = TimeSpan.FromSeconds(2);
    private const int DebugTapCount = 5;

    private readonly MainViewModel _viewModel;
    private readonly MainView _host;
    private long _lastDebugTap;
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

    // The body-bg is the page's background as much as the gradient is, so it reaches under the system
    // bars as well, by the padding the host gives the page for them (MainView.ApplySystemBars).
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PaddingProperty)
            BodyBg.Margin = new Thickness(-Padding.Left, -Padding.Top, -Padding.Right, -Padding.Bottom);
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
            await _viewModel.ToggleConnect(CancellationToken.None);
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
            _host.ShowSnackbar(reason);
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

    private void OnLanguageClick(object? sender, RoutedEventArgs e)
    {
        _host.Navigate(new LanguageView(_host));
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

    // The premium mark opens the account page signed in or not: signed out, it is where a premium
    // code is changed or removed.
    private void OnYouArePremiumClick(object? sender, RoutedEventArgs e)
    {
        _host.Navigate(new AccountView(_host));
    }

    // The account: signed in, its page; signed out, the sign-in - the dialog wherever the email form
    // is offered, else the store's own.
    private async void OnAccountClick(object? sender, RoutedEventArgs e)
    {
        try {
            if (VhApp.Account != null) {
                _host.Navigate(new AccountView(_host));
                return;
            }

            if (VhApp.HasSignInChoice) {
                await _host.ShowDialog(new SignInDialog(_host));
                return;
            }

            try {
                await _viewModel.SignIn(onPurchase: false, CancellationToken.None);
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

    // A press beside the chip; one on the chip is its click, which the chip has handled by then.
    private void OnVersionAreaPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        e.Handled = true;
        CountVersionTap();
    }

    private void OnVersionClick(object? sender, RoutedEventArgs e)
    {
        CountVersionTap();
    }

    // The developer page, opened as the web UI opens its dialog (HomePageHeader.vue): the fifth
    // tap of a run, or the first when a debug field is already set.
    private void CountVersionTap()
    {
        if (_debugTaps > 0 && Stopwatch.GetElapsedTime(_lastDebugTap) > DebugTapGap)
            _debugTaps = 0;

        _lastDebugTap = Stopwatch.GetTimestamp();
        _debugTaps++;
        if (!_viewModel.HasDebugData && _debugTaps < DebugTapCount) {
            LightVersion();
            return;
        }

        _debugTaps = 0;
        _host.Navigate(new DeveloperView(_host));
    }

    // Each tap before the fifth lights the version, so it shows the taps count. The class runs the
    // style's light, white at once and then fading; set again, it starts over for a tap that comes
    // while the last one's light still fades.
    private void LightVersion()
    {
        VersionChip.Classes.Remove("counted");
        VersionChip.Classes.Add("counted");
    }
}

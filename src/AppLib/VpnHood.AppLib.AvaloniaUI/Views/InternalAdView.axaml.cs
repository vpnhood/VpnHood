using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using VpnHood.AppLib.Abstractions.Ads;
using VpnHood.AppLib.AvaloniaUI.Helpers;
using VpnHood.AppLib.AvaloniaUI.Resources;

namespace VpnHood.AppLib.AvaloniaUI.Views;

public partial class InternalAdView : UserControl, IPage, ILeaveGuard, IDisposable
{
    private const int AdSeconds = 10;

    private readonly MainView _host;
    private readonly VpnHoodApp _app = VpnHoodApp.Instance;
    private readonly DispatcherTimer _timer;
    private int _remaining = AdSeconds;
    private bool _isDismissed;

    public InternalAdView(MainView host)
    {
        _host = host;
        InitializeComponent();
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Tick());
        _timer.Start();
        ShowRemaining();
    }

    public void FocusDefault()
    {
        CountButton.LandFocus();
    }

    // the page holds the leave until the ad has run, as the web UI's route guard does - a debug
    // build is let out
    public Task<bool> CanLeave()
    {
        return Task.FromResult(_isDismissed || _remaining <= 0 || _app.Features.IsDebugMode);
    }

    private void Tick()
    {
        if (_remaining > 0)
            _remaining--;
        ShowRemaining();
    }

    private void ShowRemaining()
    {
        var done = _remaining <= 0;
        CountButton.Content = done ? Strings.Current.Close : _remaining.ToString();
        CloseButton.IsEnabled = done;
        LearnMoreButton.IsEnabled = done;
        if (done)
            _timer.Stop();
    }

    private async void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        await Dismiss(learnMore: false);
    }

    private async void OnLearnMoreClick(object? sender, RoutedEventArgs e)
    {
        await Dismiss(learnMore: true);
    }

    private async Task Dismiss(bool learnMore)
    {
        if (_remaining > 0 && !_app.Features.IsDebugMode)
            return;

        _isDismissed = true;
        _app.AdManager.AdService.InternalAdDismiss(learnMore ? ShowAdResult.Clicked : ShowAdResult.Closed);
        if (learnMore)
            _host.Replace(new PurchaseSubscriptionView(_host, null));
        else
            _host.GoHome();
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer.Stop();
    }
}

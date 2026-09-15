using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.AvaloniaUI.Helpers;
using VpnHood.AppLib.AvaloniaUI.Resources;
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.AppLib.AvaloniaUI.Views.Dialogs;

public partial class UserReviewDialog : DialogBase
{
    private readonly MainView _host;
    private readonly VpnHoodApp _app = VpnHoodApp.Instance;
    private readonly int _recommendation;
    private int _rate;

    // recommendation 2 is the app's second ask, which the web UI shows without a way to close
    // until a face is picked; iOS keeps Later, as App Review requires a dismissible prompt
    public UserReviewDialog(MainView host, int recommendation)
    {
        _host = host;
        _recommendation = recommendation;
        InitializeComponent();
        var isIos = _app.Features.OsType == AppOsType.Ios;
        LaterButton.IsVisible = isIos;
        LaterButton.Content = Strings.Current.Later;
        CloseButton.IsVisible = recommendation != 2;
        ReviewBox.TextChanged += (_, _) => SendButton.IsEnabled = !string.IsNullOrWhiteSpace(ReviewBox.Text);
        ReviewBox.Watermark = Strings.Current.UserReviewTextPlaceholder;
    }

    public override bool CanDismiss => _recommendation != 2;

    public override void FocusDefault()
    {
        Rate3.LandFocus();
    }

    private void OnRate1Click(object? sender, RoutedEventArgs e) => Pick(1);
    private void OnRate2Click(object? sender, RoutedEventArgs e) => Pick(2);
    private void OnRate3Click(object? sender, RoutedEventArgs e) => Pick(3);

    private void Pick(int rate)
    {
        _rate = rate;
        Rate1.Opacity = rate == 1 ? 1 : 0.4;
        Rate2.Opacity = rate == 2 ? 1 : 0.4;
        Rate3.Opacity = rate == 3 ? 1 : 0.4;
        SubmitButton.IsEnabled = true;
    }

    // closed before a submit: the rate is dropped, and the ask is answered as declined
    private async void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        if (!TextStep.IsVisible)
            _rate = 0;
        await Submit(null);
    }

    private async void OnSubmitClick(object? sender, RoutedEventArgs e)
    {
        if (_rate == 3) {
            RateStep.IsVisible = false;
            Actions.IsVisible = false;
            ThanksStep.IsVisible = true;
            await Task.Delay(1500);
            await Submit(null);
            return;
        }

        RateStep.IsVisible = false;
        TextStep.IsVisible = true;
        SubmitButton.IsVisible = false;
        SendButton.IsVisible = true;
        LaterButton.Content = Strings.Current.Cancel;
        ReviewBox.LandFocus();
    }

    private async void OnSendClick(object? sender, RoutedEventArgs e)
    {
        await Submit(ReviewBox.Text);
    }

    private async Task Submit(string? text)
    {
        Close();
        try {
            _app.SetUserReview(_rate, text ?? "");
            // the store's own prompt, for a happy face
            if (_rate == 3 && _app.Services.UserReviewProvider != null)
                await _app.Services.UserReviewProvider.RequestReview(AppUiContext.RequiredContext, CancellationToken.None);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not submit the user review.");
        }
        finally {
            _host.ViewModel.Refresh();
        }
    }
}

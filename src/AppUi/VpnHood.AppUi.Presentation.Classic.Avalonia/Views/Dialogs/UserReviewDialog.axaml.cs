using Avalonia.Interactivity;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Api.App;
using VpnHood.AppUi.Common;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Helpers;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Views.Dialogs;

public partial class UserReviewDialog : DialogBase
{
    private readonly MainView _host;
    private readonly int _recommendation;
    private int _rate;

    // recommendation 2 is the app's second ask, which the web UI shows without a way to close
    // until a face is picked; iOS keeps Later, as App Review requires a dismissible prompt
    public UserReviewDialog(MainView host, int recommendation)
    {
        _host = host;
        _recommendation = recommendation;
        InitializeComponent();
        var isIos = VhApp.Features.OsType == AppOsType.Ios;
        LaterButton.IsVisible = isIos;
        LaterButton.Content = Strings.Current.Later;
        CloseButton.IsVisible = recommendation != 2;
        ReviewBox.TextChanged += (_, _) => SendButton.IsEnabled = !string.IsNullOrWhiteSpace(ReviewBox.Text);
        ReviewBox.PlaceholderText = Strings.Current.UserReviewTextPlaceholder;
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
        try {
            if (!TextStep.IsVisible)
                _rate = 0;
            await Submit(null);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnSubmitClick(object? sender, RoutedEventArgs e)
    {
        try {
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
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnSendClick(object? sender, RoutedEventArgs e)
    {
        try {
            await Submit(ReviewBox.Text);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async Task Submit(string? text)
    {
        Close();
        try {
            await VhApp.Api.App.SetUserReview(new AppUserReview { Rating = _rate, ReviewText = text ?? "" }, CancellationToken.None);
            // the store's own prompt, for a happy face
            if (_rate == 3 && VhApp.Intents.IsUserReviewSupported)
                await VhApp.Api.Intents.RequestUserReview(CancellationToken.None);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not submit the user review.");
        }
        finally {
            _host.ViewModel.Refresh();
        }
    }
}

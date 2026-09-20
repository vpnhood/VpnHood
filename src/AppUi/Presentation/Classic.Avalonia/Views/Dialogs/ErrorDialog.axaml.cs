using Avalonia.Interactivity;
using VpnHood.AppUi.Common;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Helpers;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.Sessions;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Views.Dialogs;

public partial class ErrorDialog : DialogBase
{
    private readonly MainView _host;

    public ErrorDialog(MainView host, string message, ErrorActions? actions)
    {
        _host = host;
        InitializeComponent();
        MessageText.Text = message;

        var state = VhApp.State;
        var hasProfile = VhApp.ClientProfileId != null;
        AutoButton.IsVisible = actions?.ShowChangeServerToAuto == true && hasProfile;
        TryPremiumButton.IsVisible = actions?.ShowTryPremium == true && hasProfile;
        LearnMoreButton.IsVisible = TryPremiumButton.IsVisible;
        RestoreButton.IsVisible = actions?.ShowAccessCodeActions == true;
        ChangeCodeButton.IsVisible = actions is { ShowAccessCodeActions: true, ShowChangeAccessCode: true };
        DiagnoseButton.IsVisible = actions?.ShowDiagnose == true && !state.HasDiagnoseRequested;
        // the report: the log page here, where the web UI opens the log in a browser tab; there is
        // no report upload in this head, so no Send report
        ReportButton.IsVisible = state.PromptForLog;
        Actions.IsVisible = Actions.Children.Any(x => x.IsVisible);
    }

    public override void FocusDefault()
    {
        CloseButton.LandFocus();
    }

    private async Task CloseAndClear()
    {
        await VhApp.Api.App.ClearLastError(CancellationToken.None);
        Close();
    }

    private async void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        try {
            await CloseAndClear();
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    // the automatic location, then a connect on it (ErrorDialog.changeLocationToAuto)
    private async void OnAutoClick(object? sender, RoutedEventArgs e)
    {
        try {
            if (VhApp.ClientProfileId is not { } profileId)
                return;
            await CloseAndClear();
            await _host.ViewModel.ConnectWith(new ConnectRequest(profileId, null, IsPremium: false, ConnectPlanId.Normal));
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnTryPremiumClick(object? sender, RoutedEventArgs e)
    {
        try {
            if (VhApp.ClientProfileId is not { } profileId)
                return;
            await CloseAndClear();
            await _host.ViewModel.ConnectWith(new ConnectRequest(profileId, null, IsPremium: true, ConnectPlanId.PremiumByTrial));
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnDiagnoseClick(object? sender, RoutedEventArgs e)
    {
        try {
            await CloseAndClear();
            await _host.ViewModel.Diagnose();
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnReportClick(object? sender, RoutedEventArgs e)
    {
        try {
            await CloseAndClear();
            _host.Navigate(new LogView());
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnRestoreClick(object? sender, RoutedEventArgs e)
    {
        try {
            await CloseAndClear();
            _host.Replace(new PurchaseSubscriptionView(_host, null));
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnChangeCodeClick(object? sender, RoutedEventArgs e)
    {
        try {
            await CloseAndClear();
            await _host.ShowDialog(new PremiumCodeDialog(_host));
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnLearnMoreClick(object? sender, RoutedEventArgs e)
    {
        try {
            await CloseAndClear();
            _host.Replace(new LearnMoreView(_host));
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }
}

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

    // Draws what ErrorMessages decided: the sentence, and a button per action it named; the trial
    // brings its 'learn more' link along. The report opens the log page here, where the web UI
    // opens the log in a browser tab; there is no report upload in this head, so no Send report.
    public ErrorDialog(MainView host, ErrorMessage.Dialog message)
    {
        _host = host;
        InitializeComponent();
        MessageText.Text = message.Text;

        var actions = message.Actions;
        AutoButton.IsVisible = actions.Contains(ErrorAction.ChangeServerToAuto);
        TryPremiumButton.IsVisible = actions.Contains(ErrorAction.TryPremium);
        LearnMoreButton.IsVisible = TryPremiumButton.IsVisible;
        RestoreButton.IsVisible = actions.Contains(ErrorAction.RestorePremium);
        ChangeCodeButton.IsVisible = actions.Contains(ErrorAction.ChangeAccessCode);
        DiagnoseButton.IsVisible = actions.Contains(ErrorAction.Diagnose);
        ReportButton.IsVisible = actions.Contains(ErrorAction.OpenReport);
        Actions.IsVisible = Actions.Children.Any(x => x.IsVisible);
    }

    public override void FocusDefault()
    {
        CloseButton.LandFocus();
    }

    // the app's error was cleared as it was shown (MainViewModel.RefreshPrompts), so a button only closes
    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    // the automatic location, then a connect on it (ErrorDialog.changeLocationToAuto)
    private async void OnAutoClick(object? sender, RoutedEventArgs e)
    {
        try {
            if (VhApp.VpnProfileId is not { } profileId)
                return;
            Close();
            await _host.ViewModel.ConnectWith(new ConnectRequest(profileId, null, IsPremium: false, ConnectPlanId.Normal));
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnTryPremiumClick(object? sender, RoutedEventArgs e)
    {
        try {
            if (VhApp.VpnProfileId is not { } profileId)
                return;
            Close();
            await _host.ViewModel.ConnectWith(new ConnectRequest(profileId, null, IsPremium: true, ConnectPlanId.PremiumByTrial));
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnDiagnoseClick(object? sender, RoutedEventArgs e)
    {
        try {
            Close();
            await _host.ViewModel.Diagnose();
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnReportClick(object? sender, RoutedEventArgs e)
    {
        try {
            Close();
            _host.Navigate(new LogView());
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnRestoreClick(object? sender, RoutedEventArgs e)
    {
        try {
            Close();
            _host.Replace(new PurchaseSubscriptionView(_host, null));
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnChangeCodeClick(object? sender, RoutedEventArgs e)
    {
        try {
            Close();
            await _host.ShowDialog(new PremiumCodeDialog(_host));
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnLearnMoreClick(object? sender, RoutedEventArgs e)
    {
        try {
            Close();
            _host.Replace(new LearnMoreView(_host));
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }
}

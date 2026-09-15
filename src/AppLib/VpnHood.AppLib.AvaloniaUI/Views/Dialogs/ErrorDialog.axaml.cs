using Avalonia.Interactivity;
using VpnHood.AppLib.AvaloniaUI.Helpers;
using VpnHood.Core.Common.Tokens;

namespace VpnHood.AppLib.AvaloniaUI.Views.Dialogs;

public partial class ErrorDialog : DialogBase
{
    private readonly MainView _host;

    public ErrorDialog(MainView host, string message, ErrorActions? actions)
    {
        _host = host;
        InitializeComponent();
        MessageText.Text = message;

        var state = AppData.State;
        var hasProfile = AppData.ClientProfileId != null;
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

    private void CloseAndClear()
    {
        VpnHoodApp.Instance.ClearLastError();
        Close();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        CloseAndClear();
    }

    // the automatic location, then a connect on it (ErrorDialog.changeLocationToAuto)
    private async void OnAutoClick(object? sender, RoutedEventArgs e)
    {
        if (AppData.ClientProfileId is not { } profileId)
            return;
        CloseAndClear();
        await _host.ViewModel.ConnectWith(new ConnectRequest(profileId, null, IsPremium: false, ConnectPlanId.Normal));
    }

    private async void OnTryPremiumClick(object? sender, RoutedEventArgs e)
    {
        if (AppData.ClientProfileId is not { } profileId)
            return;
        CloseAndClear();
        await _host.ViewModel.ConnectWith(new ConnectRequest(profileId, null, IsPremium: true, ConnectPlanId.PremiumByTrial));
    }

    private async void OnDiagnoseClick(object? sender, RoutedEventArgs e)
    {
        CloseAndClear();
        await _host.ViewModel.Diagnose();
    }

    private void OnReportClick(object? sender, RoutedEventArgs e)
    {
        CloseAndClear();
        _host.Navigate(new LogView());
    }

    private void OnRestoreClick(object? sender, RoutedEventArgs e)
    {
        CloseAndClear();
        _host.Replace(new PurchaseSubscriptionView(_host, null));
    }

    private async void OnChangeCodeClick(object? sender, RoutedEventArgs e)
    {
        CloseAndClear();
        await _host.ShowDialog(new PremiumCodeDialog(_host));
    }

    private void OnLearnMoreClick(object? sender, RoutedEventArgs e)
    {
        CloseAndClear();
        _host.Replace(new LearnMoreView(_host));
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using VpnHood.AppLib.Assets;
using VpnHood.AppLib.AvaloniaUI;
using VpnHood.AppLib.ClassicAvaloniaUi.Helpers;

namespace VpnHood.AppLib.ClassicAvaloniaUi.Views;

public partial class PrivacyView : UserControl, IPage
{
    private readonly MainView _host;

    public PrivacyView(MainView host)
    {
        _host = host;
        InitializeComponent();

        var s = Strings.Current;
        // a build that collects nothing offers no consent and makes no claim about data
        var isTrackerSupported = AppModel.IsAnonymousTrackerSupported;
        TrackerItem.Title = s.AllowAnonymousTracker;
        TrackerItem.Description = s.AllowAnonymousTrackerDesc;
        TrackerItem.IsOn = AppModel.UserSettings.AllowAnonymousTracker;
        TrackerItem.IsVisible = isTrackerSupported;
        NoticeText.IsVisible = isTrackerSupported;
        PolicyButton.IsVisible = AppModel.Features.PrivacyPolicyUrl != null;
        PolicyCard.IsVisible = NoticeText.IsVisible || PolicyButton.IsVisible;
    }

    public void FocusDefault()
    {
        if (TrackerItem.IsVisible) TrackerItem.FindFirstButton()?.LandFocus();
        else if (PolicyButton.IsVisible) PolicyButton.LandFocus();
        else Header.FocusBack();
    }

    private async void OnTrackerToggled(object? sender, EventArgs e)
    {
        try {
            var settings = AppModel.UserSettings;
            settings.AllowAnonymousTracker = TrackerItem.IsOn;
            await AppModel.SaveUserSettings(settings, CancellationToken.None);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnPolicyClick(object? sender, RoutedEventArgs e)
    {
        try {
            if (AppModel.Features.PrivacyPolicyUrl is { } url)
                await _host.OpenLink(url, Strings.Current.PrivacyPolicy);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }
}

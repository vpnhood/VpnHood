using Avalonia.Controls;
using Avalonia.Interactivity;
using VpnHood.AppLib.AvaloniaUI.Helpers;
using VpnHood.AppLib.AvaloniaUI.Resources;

namespace VpnHood.AppLib.AvaloniaUI.Views;

public partial class PrivacyView : UserControl, IPage
{
    private readonly MainView _host;
    private readonly VpnHoodApp _app = VpnHoodApp.Instance;

    public PrivacyView(MainView host)
    {
        _host = host;
        InitializeComponent();

        var s = Strings.Current;
        // a build that collects nothing offers no consent and makes no claim about data
        var isTrackerSupported = AppData.IsAnonymousTrackerSupported;
        TrackerItem.Title = s.AllowAnonymousTracker;
        TrackerItem.Description = s.AllowAnonymousTrackerDesc;
        TrackerItem.IsOn = _app.UserSettings.AllowAnonymousTracker;
        TrackerItem.IsVisible = isTrackerSupported;
        NoticeText.IsVisible = isTrackerSupported;
        PolicyButton.IsVisible = _app.Features.PrivacyPolicyUrl != null;
        PolicyCard.IsVisible = NoticeText.IsVisible || PolicyButton.IsVisible;
    }

    public void FocusDefault()
    {
        if (TrackerItem.IsVisible) TrackerItem.FindFirstButton()?.LandFocus();
        else if (PolicyButton.IsVisible) PolicyButton.LandFocus();
        else Header.FocusBack();
    }

    private void OnTrackerToggled(object? sender, EventArgs e)
    {
        _app.UserSettings.AllowAnonymousTracker = TrackerItem.IsOn;
        _app.SettingsService.Save();
    }

    private async void OnPolicyClick(object? sender, RoutedEventArgs e)
    {
        try {
            if (_app.Features.PrivacyPolicyUrl is { } url)
                await _host.OpenLink(url, Strings.Current.PrivacyPolicy);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }
}

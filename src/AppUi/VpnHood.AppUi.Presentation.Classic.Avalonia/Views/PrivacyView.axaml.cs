using Avalonia.Controls;
using Avalonia.Interactivity;
using VpnHood.AppUi.Common;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Helpers;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Views;

public partial class PrivacyView : UserControl, IPage
{
    private readonly MainView _host;

    public PrivacyView(MainView host)
    {
        _host = host;
        InitializeComponent();

        var s = Strings.Current;
        // a build that collects nothing offers no consent and makes no claim about data
        var isTrackerSupported = VhApp.IsAnonymousTrackerSupported;
        TrackerItem.Title = s.AllowAnonymousTracker;
        TrackerItem.Description = s.AllowAnonymousTrackerDesc;
        TrackerItem.IsOn = VhApp.UserSettings.AllowAnonymousTracker;
        TrackerItem.IsVisible = isTrackerSupported;
        NoticeText.IsVisible = isTrackerSupported;
        PolicyButton.IsVisible = VhApp.Features.PrivacyPolicyUrl != null;
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
            var settings = VhApp.UserSettings;
            settings.AllowAnonymousTracker = TrackerItem.IsOn;
            await VhApp.SaveUserSettings(settings, CancellationToken.None);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnPolicyClick(object? sender, RoutedEventArgs e)
    {
        try {
            if (VhApp.Features.PrivacyPolicyUrl is { } url)
                await _host.OpenLink(url, Strings.Current.PrivacyPolicy);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }
}

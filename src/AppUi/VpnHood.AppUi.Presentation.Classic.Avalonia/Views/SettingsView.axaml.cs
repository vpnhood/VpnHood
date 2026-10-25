using Avalonia.Controls;
using VpnHood.AppLib.Api.App;
using VpnHood.AppUi.Common;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Helpers;
using VpnHood.AppLib.Api.Settings;
using Avalonia;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Views;

public partial class SettingsView : UserControl, IPage
{
    private readonly MainView _host;

    public SettingsView(MainView host)
    {
        _host = host;
        InitializeComponent();
        Fill();
    }

    // read again on arrival and on every return: the rows say what the pages behind them changed
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Fill();
        if (AutomaticUpdatesItem.IsVisible)
            _ = LoadAutomaticUpdates();
    }

    private void Fill()
    {
        var s = Strings.Current;
        var state = VhApp.State;
        var settings = VhApp.UserSettings;
        var intents = VhApp.Intents;
        var split = state.SplitTunnelingState;

        AppSection.Title = s.AppSettings;
        LanguageItem.Title = s.Language;
        LanguageItem.Subtitle = s.AppLanguageDesc;
        LanguageItem.SetStatus(settings.CultureCode != null, state.CurrentUiCultureInfo.NativeName, s.SystemDefaultLanguage);

        NotificationsItem.Title = s.Notifications;
        NotificationsItem.Subtitle = s.NotificationsDesc;
        NotificationsItem.SetStatus(VhApp.IsNotificationEnabled(state), s.On, s.Off);
        NotificationsItem.IsVisible = intents.IsAppNotificationSettingsSupported;

        QuickLaunchItem.Title = s.QuickLaunch;
        QuickLaunchItem.Subtitle = s.QuickLaunchDesc;
        QuickLaunchItem.IsPremium = VhApp.IsPremiumFeature(AppFeature.QuickLaunch);
        QuickLaunchItem.IsVisible = intents.IsQuickLaunchSupported;

        AutomaticUpdatesItem.Title = s.AutomaticUpdates;
        AutomaticUpdatesItem.Description = s.AutomaticUpdatesDesc;
        AutomaticUpdatesItem.IsVisible = VhApp.Features.IsAutomaticUpdateSupported;
        AppSection.IsVisible = true;

        ConnectivitySection.Title = s.Connectivity;
        ProxiesItem.Title = s.Proxies;
        ProxiesItem.Subtitle = s.ProxiesDesc;
        var proxyMode = settings.ProxySettings.Mode;
        ProxiesItem.SetStatus(proxyMode != AppProxyMode.NoProxy, proxyMode == AppProxyMode.Device ? s.System : s.Manual, s.NoProxy);

        SplitTunnelingItem.Title = s.SplitTunneling;
        SplitTunnelingItem.Subtitle = s.SplitTunnelingDesc;
        SplitTunnelingItem.SetStatus(split.IsEnabled || split.IsLocalNetworkSplit, split.IsEnabled ? s.On : s.LocalNetwork, s.Off);
        SplitTunnelingItem.SetWarning(split.IsSplittingTraffic ? s.LeakIp : null);

        DnsItem.Title = s.Dns;
        DnsItem.Subtitle = s.DnsDesc;
        DnsItem.SetStatus(VhApp.IsDnsCustomized(state), VhApp.IsPrivateDnsCustomized(state) ? s.PrivateDns : s.Custom, s.Default);
        DnsItem.IsPremium = VhApp.IsPremiumFeature(AppFeature.CustomDns);

        PrivacySection.Title = s.PrivacyAndSecurity;
        PrivacyItem.Title = s.Privacy;
        // The description promises control over anonymous data, so it is only true where there is
        // any to control: a build that collects nothing has one link behind this row, and claiming
        // otherwise sends people looking for a switch that was never built.
        PrivacyItem.Subtitle = VhApp.IsAnonymousTrackerSupported ? s.PrivacyDesc : "";
        KillSwitchItem.Title = s.KillSwitch;
        KillSwitchItem.Subtitle = s.KillSwitchDesc;
        KillSwitchItem.IsVisible = intents.IsKillSwitchSettingsSupported;
        AlwaysOnItem.Title = s.AlwaysOn;
        AlwaysOnItem.Subtitle = s.AlwaysOnDesc;
        AlwaysOnItem.IsPremium = VhApp.IsPremiumFeature(AppFeature.AlwaysOn);
        // the app's own switch (a desktop's service), or the device's setting (Android's), whose state
        // the app cannot read
        var alwaysOnSupport = VhApp.Features.AlwaysOnSupport;
        AlwaysOnItem.IsVisible = alwaysOnSupport != AlwaysOnSupport.None;
        if (alwaysOnSupport == AlwaysOnSupport.App)
            AlwaysOnItem.SetStatus(settings.AlwaysOn, s.On, s.Off);
    }

    public void FocusDefault()
    {
        LanguageItem.Focus();
        Header.FocusBack();
        LanguageItem.FindFirstButton()?.LandFocus();
    }

    private void OnLanguageClick(object? sender, EventArgs e) => _host.Navigate(new LanguageView(_host));
    private void OnNotificationsClick(object? sender, EventArgs e) => _host.Navigate(FeaturePages.Notifications(_host));
    private void OnQuickLaunchClick(object? sender, EventArgs e) => _host.Navigate(FeaturePages.QuickLaunch(_host));
    private void OnProxiesClick(object? sender, EventArgs e) => _host.Navigate(new ProxiesView(_host));
    private void OnSplitTunnelingClick(object? sender, EventArgs e) => _host.Navigate(new SplitTunnelingView(_host));
    private void OnDnsClick(object? sender, EventArgs e) => _host.Navigate(DnsView.Create(_host));
    private void OnPrivacyClick(object? sender, EventArgs e) => _host.Navigate(new PrivacyView(_host));
    private void OnKillSwitchClick(object? sender, EventArgs e) => _host.Navigate(FeaturePages.KillSwitch(_host));
    private void OnAlwaysOnClick(object? sender, EventArgs e) => _host.Navigate(FeaturePages.AlwaysOn(_host));

    // The platform's switch, a desktop's timer or task, read at every arrival: its installer and its own tools
    // turn it too. Held until read, so a press never answers a state the row did not show.
    private async Task LoadAutomaticUpdates()
    {
        AutomaticUpdatesItem.IsDisabled = true;
        try {
            AutomaticUpdatesItem.IsOn = await VhApp.Api.App.GetAutomaticUpdate(CancellationToken.None);
            AutomaticUpdatesItem.IsDisabled = false;
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnAutomaticUpdatesToggled(object? sender, EventArgs e)
    {
        var isOn = AutomaticUpdatesItem.IsOn;
        AutomaticUpdatesItem.IsDisabled = true;
        try {
            await VhApp.Api.App.SetAutomaticUpdate(isOn, CancellationToken.None);
        }
        catch (Exception ex) {
            AutomaticUpdatesItem.IsOn = !isOn;
            await this.ReportError(ex);
        }
        finally {
            AutomaticUpdatesItem.IsDisabled = false;
        }
    }
}

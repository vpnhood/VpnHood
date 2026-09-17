using Avalonia.Controls;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Assets;
using VpnHood.AppLib.AvaloniaUI;
using VpnHood.AppLib.ClassicAvaloniaUi.Helpers;
using VpnHood.AppLib.Api.Settings;

namespace VpnHood.AppLib.ClassicAvaloniaUi.Views;

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
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Fill();
    }

    private void Fill()
    {
        var s = Strings.Current;
        var state = AppModel.State;
        var settings = AppModel.UserSettings;
        var intents = AppModel.Intents;
        var split = state.SplitTunnelingState;

        AppSection.Title = s.AppSettings;
        LanguageItem.Title = s.Language;
        LanguageItem.Subtitle = s.AppLanguageDesc;
        LanguageItem.SetStatus(settings.CultureCode != null, state.CurrentUiCultureInfo.NativeName, s.SystemDefaultLanguage);
        // the web UI asks its resolved locale, which is plain 'en' for any English culture
        LanguageItem.ShowLanguageMore = !state.CurrentUiCultureInfo.Code.StartsWith("en", StringComparison.OrdinalIgnoreCase);

        NotificationsItem.Title = s.Notifications;
        NotificationsItem.Subtitle = s.NotificationsDesc;
        NotificationsItem.SetStatus(AppModel.IsNotificationEnabled(state), s.On, s.Off);
        NotificationsItem.IsVisible = intents.IsAppNotificationSettingsSupported;

        QuickLaunchItem.Title = s.QuickLaunch;
        QuickLaunchItem.Subtitle = s.QuickLaunchDesc;
        QuickLaunchItem.IsPremium = AppModel.IsPremiumFeature(AppFeature.QuickLaunch);
        QuickLaunchItem.IsVisible = intents.IsQuickLaunchSupported;
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
        DnsItem.SetStatus(AppModel.IsDnsCustomized(state), AppModel.IsPrivateDnsCustomized(state) ? s.PrivateDns : s.Custom, s.Default);
        DnsItem.IsPremium = AppModel.IsPremiumFeature(AppFeature.CustomDns);

        PrivacySection.Title = s.PrivacyAndSecurity;
        PrivacyItem.Title = s.Privacy;
        PrivacyItem.Subtitle = s.PrivacyDesc;
        KillSwitchItem.Title = s.KillSwitch;
        KillSwitchItem.Subtitle = s.KillSwitchDesc;
        KillSwitchItem.IsVisible = intents.IsKillSwitchSettingsSupported;
        AlwaysOnItem.Title = s.AlwaysOn;
        AlwaysOnItem.Subtitle = s.AlwaysOnDesc;
        AlwaysOnItem.IsPremium = AppModel.IsPremiumFeature(AppFeature.AlwaysOn);
        AlwaysOnItem.IsVisible = intents.IsAlwaysOnSettingsSupported;
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
}

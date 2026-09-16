using Avalonia;
using Avalonia.Controls;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Assets;
using VpnHood.AppLib.ClassicAvaloniaUi.Controls;
using VpnHood.AppLib.ClassicAvaloniaUi.Helpers;
using VpnHood.AppLib.Contracts.App;
using VpnHood.Core.Client.Abstractions;
using AppData = VpnHood.AppLib.AvaloniaUI.AppData;

namespace VpnHood.AppLib.ClassicAvaloniaUi.Views;

public partial class SplitTunnelingView : UserControl, IPage
{
    private readonly MainView _host;

    public SplitTunnelingView(MainView host)
    {
        _host = host;
        InitializeComponent();
        Fill();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Fill();
    }

    private void Fill()
    {
        var s = Strings.Current;
        var state = AppData.State;
        var split = state.SplitTunnelingState;
        var features = AppData.Features;

        EnabledItem.Title = s.SplitTunnelingToggle;
        EnabledItem.IsOn = AppData.UserSettings.SplitTunneling.Enabled;
        EnabledItem.Warning = split.IsEnabled ? s.LeakIp : null;
        EnabledItem.Description = split.IsEnabled ? s.SplitTunnelingToggleDesc : s.SplitTunnelingDisabledDesc;
        ServerSplitAlert.IsVisible = split.IsSplitByServer;

        var showApps = features.IsExcludeAppsSupported || features.IsIncludeAppsSupported;
        AppsSection.Title = s.AppsAndDomains;
        AppsItem.Title = s.SplitApps;
        AppsItem.Subtitle = s.SplitAppsShortDesc;
        AppsItem.SetStatus(split.IsAppSplit, AppText.SplitAppsStatusText(), s.Off, StatusColor.Switch);
        AppsItem.IsVisible = showApps;

        DomainsItem.Title = s.SplitDomains;
        DomainsItem.Subtitle = s.SplitDomainsShortDesc;
        DomainsItem.IsPremium = AppData.IsPremiumFeature(AppFeature.SplitDomain);
        DomainsItem.SetStatus(split.IsDomainSplit, s.On, s.Off);
        DomainsItem.IsDisabled = !split.IsEnabled;

        IpSection.Title = s.IpAddresses;
        IpsViaDeviceItem.Title = s.SplitIpsViaDevice;
        IpsViaDeviceItem.Subtitle = s.SplitIpsViaDeviceShortDesc;
        IpsViaDeviceItem.IsPremium = AppData.IsPremiumFeature(AppFeature.SplitIpViaDevice);
        IpsViaDeviceItem.SetStatus(split.IsIpViaDeviceSplit, s.On, s.Off);
        IpsViaDeviceItem.IsDisabled = !split.IsEnabled;

        IpsViaAppItem.Title = s.SplitIpsViaApp;
        IpsViaAppItem.Subtitle = s.SplitIpsViaAppShortDesc;
        IpsViaAppItem.IsPremium = AppData.IsPremiumFeature(AppFeature.SplitIpViaApp);
        IpsViaAppItem.SetStatus(split.IsIpViaAppSplit, s.On, s.Off);
        IpsViaAppItem.IsDisabled = !split.IsEnabled;

        Ipv6Item.Title = s.SplitIpv6;
        Ipv6Item.Subtitle = s.SplitIpv6ShortDesc;
        Ipv6Item.SetStatus(split.UnsupportedIpV6Mode == SplitUnsupportedIpMode.Exclude, s.SplitIpv6Exclude, s.Off, StatusColor.Warning);
        Ipv6Item.IsDisabled = !split.IsEnabled;

        LocationSection.Title = s.Locations;
        LocalNetworkItem.Title = s.SplitLocalNetwork;
        LocalNetworkItem.Subtitle = s.SplitLocalNetworkShortDesc;
        LocalNetworkItem.SetStatus(split.IsLocalNetworkSplit, s.On, s.Off);

        CountriesItem.Title = s.SplitCountries;
        CountriesItem.Subtitle = s.SplitCountriesShortDesc;
        CountriesItem.IsPremium = AppData.IsPremiumFeature(AppFeature.SplitCountry);
        CountriesItem.SetStatus(split.IsCountrySplit, AppText.SplitCountryStatusText(state), s.Off, StatusColor.Switch);
        CountriesItem.IsDisabled = !split.IsEnabled;

        DnsSection.Title = s.Dns;
        DnsItem.Title = s.SplitDns;
        DnsItem.Subtitle = s.SplitDnsShortDesc;
        DnsItem.SetStatus(split.DnsMode == SplitDnsMode.DefaultRoute, s.SplitDnsDefaultRoute, s.Off, StatusColor.Warning);
        DnsItem.IsDisabled = !split.IsEnabled;
    }

    public void FocusDefault()
    {
        EnabledItem.FindFirstButton()?.LandFocus();
    }

    private async void OnEnabledToggled(object? sender, EventArgs e)
    {
        try {
            var settings = AppData.UserSettings;
            settings.SplitTunneling.Enabled = EnabledItem.IsOn;
            await AppData.SaveUserSettings(settings, CancellationToken.None);
            Fill();
            _host.ViewModel.Refresh();
        }
        catch (Exception ex) {
            await _host.ProcessError(ex);
        }
    }

    private void OnAppsClick(object? sender, EventArgs e) => _host.Navigate(new SplitAppsView(_host));
    private void OnDomainsClick(object? sender, EventArgs e) => _host.Navigate(SplitDomainsView.Create(_host));
    private void OnIpsViaDeviceClick(object? sender, EventArgs e) => _host.Navigate(SplitIpsViaDeviceView.Create(_host));
    private void OnIpsViaAppClick(object? sender, EventArgs e) => _host.Navigate(SplitIpsViaAppView.Create(_host));
    private void OnIpv6Click(object? sender, EventArgs e) => _host.Navigate(new SplitIpv6View(_host));
    private void OnLocalNetworkClick(object? sender, EventArgs e) => _host.Navigate(new SplitLocalNetworkView(_host));
    private void OnCountriesClick(object? sender, EventArgs e) => _host.Navigate(new SplitCountriesView(_host));
    private void OnDnsClick(object? sender, EventArgs e) => _host.Navigate(new SplitDnsView(_host));
}

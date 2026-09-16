using System.Net;
using Avalonia.Controls;
using Avalonia.Interactivity;
using VpnHood.AppLib.AvaloniaUI.Helpers;
using VpnHood.AppLib.AvaloniaUI.Resources;
using VpnHood.AppLib.Settings;

namespace VpnHood.AppLib.AvaloniaUI.Views;

public partial class DnsView : UserControl, IPage, ILeaveGuard
{
    private readonly MainView _host;
    private DnsMode _mode;

    private DnsView(MainView host)
    {
        _host = host;
        InitializeComponent();
        var s = Strings.Current;
        var state = AppData.State;
        var settings = AppData.UserSettings;
        _mode = settings.DnsMode;

        // the private DNS card: off, the device's automatic mode, or a provider the person chose
        var isActive = AppData.IsPrivateDnsActive(state);
        var isCustomized = AppData.IsPrivateDnsCustomized(state);
        PrivateDnsCard.IsVisible = AppData.Intents.IsPrivateDnsSettingsSupported;
        PrivateDnsStatus.Text = !isActive ? s.Off : isCustomized ? s.On : s.Auto;
        PrivateDnsChip.Classes.Set("status-on", isCustomized);
        PrivateDnsChip.Classes.Set("status-off", !isActive);
        PrivateDnsCrown.IsVisible = AppData.ShowCrown(AppFeature.CustomDns);
        ProviderText.Text = state.SystemPrivateDns?.Provider;
        ProviderText.IsVisible = !string.IsNullOrEmpty(state.SystemPrivateDns?.Provider);

        AdapterCrown.IsVisible = AppData.ShowCrown(AppFeature.CustomDns);
        DefaultRow.Title = s.Default;
        DefaultRow.Description = s.AdapterDnsAutoDesc;
        CustomRow.Title = s.Custom;
        CustomRow.Description = s.AdapterDnsCustomDesc;
        Dns1Box.Text = settings.DnsServers.ElementAtOrDefault(0)?.ToString();
        Dns2Box.Text = settings.DnsServers.ElementAtOrDefault(1)?.ToString();
        Show();
    }

    // the pitch while the feature is sold and this session has not bought it (dns/index.vue)
    public static IPage Create(MainView host)
    {
        return AppData.IsPremiumFeatureAllowed(AppFeature.CustomDns)
            ? new DnsView(host)
            : FeaturePages.PremiumPitch(host, Strings.Current.Dns, Strings.Current.DnsDesc, "private-dns.webp", AppFeature.CustomDns);
    }

    // connected to a server that overrides the person's DNS: the radios are read-only meanwhile
    private bool IsEnforcedByServer =>
        AppData.IsConnected(AppData.State) && _mode == DnsMode.AdapterDns && AppData.State.SessionInfo?.DnsConfig.IsUserSuppressed == true;

    private void Show()
    {
        var isCustom = _mode == DnsMode.AdapterDns;
        DefaultRow.IsChecked = !isCustom;
        CustomRow.IsChecked = isCustom;
        var isEnforced = IsEnforcedByServer;
        DefaultRow.IsDisabled = isEnforced;
        CustomRow.IsDisabled = isEnforced;
        EnforcedAlert.IsVisible = isEnforced;
        PrivateDnsAlert.IsVisible = isCustom && AppData.IsPrivateDnsCustomized(AppData.State);
        CustomPanel.IsVisible = isCustom;
    }

    public void FocusDefault()
    {
        if (PrivateDnsCard.IsVisible) PrivateDnsCard.LandFocus();
        else if (!DefaultRow.IsDisabled) (DefaultRow.IsChecked ? DefaultRow : CustomRow).LandFocus();
        else Header.FocusBack();
    }

    private void OnPrivateDnsClick(object? sender, RoutedEventArgs e)
    {
        _host.Navigate(FeaturePages.PrivateDns(_host));
    }

    private void OnDefaultClick(object? sender, EventArgs e)
    {
        _mode = DnsMode.Default;
        Show();
    }

    private void OnCustomClick(object? sender, EventArgs e)
    {
        _mode = DnsMode.AdapterDns;
        Show();
        Dns1Box.LandFocus();
    }

    private void OnDnsChanged(object? sender, TextChangedEventArgs e)
    {
        Validate();
    }

    // DNS 1 is required once Custom is chosen; both must be addresses (dns/index.vue's rules)
    private bool Validate()
    {
        var s = Strings.Current;
        var dns1 = Dns1Box.Text?.Trim();
        var dns2 = Dns2Box.Text?.Trim();
        var error1 = _mode == DnsMode.AdapterDns && string.IsNullOrEmpty(dns1) ? s.Dns1Required
            : !string.IsNullOrEmpty(dns1) && !IPAddress.TryParse(dns1, out _) ? s.InvalidIp
            : null;
        var error2 = !string.IsNullOrEmpty(dns2) && !IPAddress.TryParse(dns2, out _) ? s.InvalidIp : null;
        Dns1Error.Text = error1;
        Dns1Error.IsVisible = error1 != null;
        Dns2Error.Text = error2;
        Dns2Error.IsVisible = error2 != null;
        return error1 == null && error2 == null;
    }

    // An unfinished Custom entry never holds the page: nothing is saved until here, so an invalid
    // form is dropped with a word, and the mode saved before the radio moved stays in force.
    public async Task<bool> CanLeave()
    {
        if (_mode == DnsMode.AdapterDns && !Validate()) {
            _host.ShowSnackbar(Strings.Current.CustomDnsDiscarded, SnackbarKind.Warning);
            return true;
        }

        try {
            var settings = AppData.UserSettings;
            settings.DnsMode = _mode;
            settings.DnsServers = [.. new[] { Dns1Box.Text, Dns2Box.Text }
                .Select(x => x?.Trim())
                .Where(x => !string.IsNullOrEmpty(x))
                .Select(x => IPAddress.Parse(x ?? ""))];
            await AppData.SaveUserSettings(settings, CancellationToken.None);
            _host.ViewModel.Refresh();
            return true;
        }
        catch (Exception ex) {
            await _host.ProcessError(ex);
            return false;
        }
    }
}

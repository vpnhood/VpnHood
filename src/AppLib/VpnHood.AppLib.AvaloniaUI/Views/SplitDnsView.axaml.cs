using Avalonia.Controls;
using VpnHood.AppLib.AvaloniaUI.Resources;
using VpnHood.Core.Client.Abstractions;

namespace VpnHood.AppLib.AvaloniaUI.Views;

public partial class SplitDnsView : UserControl, IPage
{
    private readonly VpnHoodApp _app = VpnHoodApp.Instance;

    public SplitDnsView(MainView host)
    {
        _ = host;
        InitializeComponent();
        var s = Strings.Current;
        IncludeAllRow.Title = s.SplitDnsIncludeAll;
        IncludeAllRow.Description = s.SplitDnsIncludeAllDesc;
        IncludeAllRow.ChipLabel = s.Recommended;
        DefaultRouteRow.Title = s.SplitDnsDefaultRoute;
        DefaultRouteRow.Description = s.SplitDnsDefaultRouteDesc;
        Show();
    }

    private void Show()
    {
        var split = _app.UserSettings.SplitTunneling;
        IncludeAllRow.IsChecked = split.DnsMode == SplitDnsMode.IncludeAll;
        DefaultRouteRow.IsChecked = split.DnsMode == SplitDnsMode.DefaultRoute;
        IncludeAllRow.IsDisabled = !split.Enabled;
        DefaultRouteRow.IsDisabled = !split.Enabled;
        Card.Opacity = split.Enabled ? 1 : 0.5;
        LeakAlert.IsVisible = split is { Enabled: true, DnsMode: SplitDnsMode.DefaultRoute };
    }

    public void FocusDefault()
    {
        if (_app.UserSettings.SplitTunneling.Enabled) (IncludeAllRow.IsChecked ? IncludeAllRow : DefaultRouteRow).LandFocus();
        else Header.FocusBack();
    }

    private void Choose(SplitDnsMode mode)
    {
        _app.UserSettings.SplitTunneling.DnsMode = mode;
        _app.SettingsService.Save();
        Show();
    }

    private void OnIncludeAllClick(object? sender, EventArgs e) => Choose(SplitDnsMode.IncludeAll);
    private void OnDefaultRouteClick(object? sender, EventArgs e) => Choose(SplitDnsMode.DefaultRoute);

    private void OnTurnedOn(object? sender, EventArgs e)
    {
        Show();
    }
}

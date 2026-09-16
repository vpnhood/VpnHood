using Avalonia.Controls;
using VpnHood.AppLib.AvaloniaUI.Helpers;
using VpnHood.AppLib.AvaloniaUI.Resources;
using VpnHood.Core.Client.Abstractions;

namespace VpnHood.AppLib.AvaloniaUI.Views;

public partial class SplitDnsView : UserControl, IPage
{
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
        var split = AppData.UserSettings.SplitTunneling;
        IncludeAllRow.IsChecked = split.DnsMode == SplitDnsMode.IncludeAll;
        DefaultRouteRow.IsChecked = split.DnsMode == SplitDnsMode.DefaultRoute;
        IncludeAllRow.IsDisabled = !split.Enabled;
        DefaultRouteRow.IsDisabled = !split.Enabled;
        Card.Opacity = split.Enabled ? 1 : 0.5;
        LeakAlert.IsVisible = split is { Enabled: true, DnsMode: SplitDnsMode.DefaultRoute };
    }

    public void FocusDefault()
    {
        if (AppData.UserSettings.SplitTunneling.Enabled) (IncludeAllRow.IsChecked ? IncludeAllRow : DefaultRouteRow).LandFocus();
        else Header.FocusBack();
    }

    private async void Choose(SplitDnsMode mode)
    {
        try {
            var settings = AppData.UserSettings;
            settings.SplitTunneling.DnsMode = mode;
            await AppData.SaveUserSettings(settings, CancellationToken.None);
            Show();
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private void OnIncludeAllClick(object? sender, EventArgs e) => Choose(SplitDnsMode.IncludeAll);
    private void OnDefaultRouteClick(object? sender, EventArgs e) => Choose(SplitDnsMode.DefaultRoute);

    private void OnTurnedOn(object? sender, EventArgs e)
    {
        Show();
    }
}

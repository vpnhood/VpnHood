using Avalonia.Controls;
using VpnHood.AppLib.AvaloniaUI.Helpers;
using VpnHood.AppLib.AvaloniaUI.Resources;
using VpnHood.Core.Client.Abstractions;

namespace VpnHood.AppLib.AvaloniaUI.Views;

public partial class SplitIpv6View : UserControl, IPage
{
    public SplitIpv6View(MainView host)
    {
        _ = host;
        InitializeComponent();
        var s = Strings.Current;
        RichText.Apply(DescriptionText, s.SplitIpv6Desc);
        BlockRow.Title = s.SplitIpv6Block;
        BlockRow.Description = s.SplitIpv6BlockDesc;
        BlockRow.ChipLabel = s.Recommended;
        ExcludeRow.Title = s.SplitIpv6Exclude;
        ExcludeRow.Description = s.SplitIpv6ExcludeDesc;
        Show();
    }

    private void Show()
    {
        var split = AppData.UserSettings.SplitTunneling;
        BlockRow.IsChecked = split.UnsupportedIpV6Mode == SplitUnsupportedIpMode.Block;
        ExcludeRow.IsChecked = split.UnsupportedIpV6Mode == SplitUnsupportedIpMode.Exclude;
        BlockRow.IsDisabled = !split.Enabled;
        ExcludeRow.IsDisabled = !split.Enabled;
        Card.Opacity = split.Enabled ? 1 : 0.5;
        LeakAlert.IsVisible = split is { Enabled: true, UnsupportedIpV6Mode: SplitUnsupportedIpMode.Exclude };
    }

    public void FocusDefault()
    {
        if (AppData.UserSettings.SplitTunneling.Enabled) (BlockRow.IsChecked ? BlockRow : ExcludeRow).LandFocus();
        else Header.FocusBack();
    }

    private async void Choose(SplitUnsupportedIpMode mode)
    {
        try {
            var settings = AppData.UserSettings;
            settings.SplitTunneling.UnsupportedIpV6Mode = mode;
            await AppData.SaveUserSettings(settings, CancellationToken.None);
            Show();
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private void OnBlockClick(object? sender, EventArgs e) => Choose(SplitUnsupportedIpMode.Block);
    private void OnExcludeClick(object? sender, EventArgs e) => Choose(SplitUnsupportedIpMode.Exclude);

    private void OnTurnedOn(object? sender, EventArgs e)
    {
        Show();
    }
}

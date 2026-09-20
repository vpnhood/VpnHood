using Avalonia.Controls;
using VpnHood.AppUi.Services;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Helpers;

using VpnHood.AppLib.Api.SplitTunneling;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Views;

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
        var split = AppModel.UserSettings.SplitTunneling;
        BlockRow.IsChecked = split.UnsupportedIpV6Mode == SplitUnsupportedIpMode.Block;
        ExcludeRow.IsChecked = split.UnsupportedIpV6Mode == SplitUnsupportedIpMode.Exclude;
        BlockRow.IsDisabled = !split.Enabled;
        ExcludeRow.IsDisabled = !split.Enabled;
        Card.Opacity = split.Enabled ? 1 : 0.5;
        LeakAlert.IsVisible = split is { Enabled: true, UnsupportedIpV6Mode: SplitUnsupportedIpMode.Exclude };
    }

    public void FocusDefault()
    {
        if (AppModel.UserSettings.SplitTunneling.Enabled) (BlockRow.IsChecked ? BlockRow : ExcludeRow).LandFocus();
        else Header.FocusBack();
    }

    private async void Choose(SplitUnsupportedIpMode mode)
    {
        try {
            var settings = AppModel.UserSettings;
            settings.SplitTunneling.UnsupportedIpV6Mode = mode;
            await AppModel.SaveUserSettings(settings, CancellationToken.None);
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

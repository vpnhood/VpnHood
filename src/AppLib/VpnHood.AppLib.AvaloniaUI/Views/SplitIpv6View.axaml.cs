using Avalonia.Controls;
using VpnHood.AppLib.AvaloniaUI.Helpers;
using VpnHood.AppLib.AvaloniaUI.Resources;
using VpnHood.Core.Client.Abstractions;

namespace VpnHood.AppLib.AvaloniaUI.Views;

public partial class SplitIpv6View : UserControl, IPage
{
    private readonly VpnHoodApp _app = VpnHoodApp.Instance;

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
        var split = _app.UserSettings.SplitTunneling;
        BlockRow.IsChecked = split.UnsupportedIpV6Mode == SplitUnsupportedIpMode.Block;
        ExcludeRow.IsChecked = split.UnsupportedIpV6Mode == SplitUnsupportedIpMode.Exclude;
        BlockRow.IsDisabled = !split.Enabled;
        ExcludeRow.IsDisabled = !split.Enabled;
        Card.Opacity = split.Enabled ? 1 : 0.5;
        LeakAlert.IsVisible = split is { Enabled: true, UnsupportedIpV6Mode: SplitUnsupportedIpMode.Exclude };
    }

    public void FocusDefault()
    {
        if (_app.UserSettings.SplitTunneling.Enabled) (BlockRow.IsChecked ? BlockRow : ExcludeRow).LandFocus();
        else Header.FocusBack();
    }

    private void Choose(SplitUnsupportedIpMode mode)
    {
        _app.UserSettings.SplitTunneling.UnsupportedIpV6Mode = mode;
        _app.SettingsService.Save();
        Show();
    }

    private void OnBlockClick(object? sender, EventArgs e) => Choose(SplitUnsupportedIpMode.Block);
    private void OnExcludeClick(object? sender, EventArgs e) => Choose(SplitUnsupportedIpMode.Exclude);

    private void OnTurnedOn(object? sender, EventArgs e)
    {
        Show();
    }
}

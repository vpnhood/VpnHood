using Avalonia.Controls;
using VpnHood.AppLib.AvaloniaUI.Helpers;
using VpnHood.AppLib.AvaloniaUI.Resources;

namespace VpnHood.AppLib.AvaloniaUI.Views;

public partial class SplitLocalNetworkView : UserControl, IPage
{
    private readonly VpnHoodApp _app = VpnHoodApp.Instance;

    public SplitLocalNetworkView(MainView host)
    {
        _ = host;
        InitializeComponent();
        var s = Strings.Current;
        var isAvailable = AppData.IsLocalNetworkAvailable(_app.State);
        EnforcedAlert.IsVisible = !isAvailable;
        EnabledItem.Title = s.SplitLocalNetwork;
        EnabledItem.Description = s.SplitLocalNetworkDesc;
        EnabledItem.IsOn = _app.UserSettings.SplitTunneling.UseLocalNetwork;
        EnabledItem.IsDisabled = !isAvailable;
    }

    public void FocusDefault()
    {
        if (!EnabledItem.IsDisabled) EnabledItem.FindFirstButton()?.LandFocus();
        else Header.FocusBack();
    }

    private void OnToggled(object? sender, EventArgs e)
    {
        _app.UserSettings.SplitTunneling.UseLocalNetwork = EnabledItem.IsOn;
        _app.SettingsService.Save();
    }
}

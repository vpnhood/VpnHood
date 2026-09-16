using Avalonia.Controls;
using VpnHood.AppLib.AvaloniaUI.Helpers;
using VpnHood.AppLib.AvaloniaUI.Resources;

namespace VpnHood.AppLib.AvaloniaUI.Views;

public partial class SplitLocalNetworkView : UserControl, IPage
{
    public SplitLocalNetworkView(MainView host)
    {
        _ = host;
        InitializeComponent();
        var s = Strings.Current;
        var isAvailable = AppData.IsLocalNetworkAvailable(AppData.State);
        EnforcedAlert.IsVisible = !isAvailable;
        EnabledItem.Title = s.SplitLocalNetwork;
        EnabledItem.Description = s.SplitLocalNetworkDesc;
        EnabledItem.IsOn = AppData.UserSettings.SplitTunneling.UseLocalNetwork;
        EnabledItem.IsDisabled = !isAvailable;
    }

    public void FocusDefault()
    {
        if (!EnabledItem.IsDisabled) EnabledItem.FindFirstButton()?.LandFocus();
        else Header.FocusBack();
    }

    private async void OnToggled(object? sender, EventArgs e)
    {
        try {
            var settings = AppData.UserSettings;
            settings.SplitTunneling.UseLocalNetwork = EnabledItem.IsOn;
            await AppData.SaveUserSettings(settings, CancellationToken.None);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }
}

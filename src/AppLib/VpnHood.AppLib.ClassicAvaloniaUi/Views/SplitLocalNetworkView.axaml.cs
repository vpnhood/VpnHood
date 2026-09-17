using Avalonia.Controls;
using VpnHood.AppLib.Assets;
using VpnHood.AppLib.AvaloniaUI;
using VpnHood.AppLib.ClassicAvaloniaUi.Helpers;

namespace VpnHood.AppLib.ClassicAvaloniaUi.Views;

public partial class SplitLocalNetworkView : UserControl, IPage
{
    public SplitLocalNetworkView(MainView host)
    {
        _ = host;
        InitializeComponent();
        var s = Strings.Current;
        var isAvailable = AppModel.IsLocalNetworkAvailable(AppModel.State);
        EnforcedAlert.IsVisible = !isAvailable;
        EnabledItem.Title = s.SplitLocalNetwork;
        EnabledItem.Description = s.SplitLocalNetworkDesc;
        EnabledItem.IsOn = AppModel.UserSettings.SplitTunneling.UseLocalNetwork;
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
            var settings = AppModel.UserSettings;
            settings.SplitTunneling.UseLocalNetwork = EnabledItem.IsOn;
            await AppModel.SaveUserSettings(settings, CancellationToken.None);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }
}

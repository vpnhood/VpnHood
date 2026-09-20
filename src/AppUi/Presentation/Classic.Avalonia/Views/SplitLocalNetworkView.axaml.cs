using Avalonia.Controls;
using VpnHood.AppUi.Common;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Helpers;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Views;

public partial class SplitLocalNetworkView : UserControl, IPage
{
    public SplitLocalNetworkView(MainView host)
    {
        _ = host;
        InitializeComponent();
        var s = Strings.Current;
        var isAvailable = VhApp.IsLocalNetworkAvailable(VhApp.State);
        EnforcedAlert.IsVisible = !isAvailable;
        EnabledItem.Title = s.SplitLocalNetwork;
        EnabledItem.Description = s.SplitLocalNetworkDesc;
        EnabledItem.IsOn = VhApp.UserSettings.SplitTunneling.UseLocalNetwork;
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
            var settings = VhApp.UserSettings;
            settings.SplitTunneling.UseLocalNetwork = EnabledItem.IsOn;
            await VhApp.SaveUserSettings(settings, CancellationToken.None);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }
}

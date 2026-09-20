using Avalonia.Controls;
using Avalonia.Interactivity;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Helpers;
using VpnHood.AppUi.Common;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Controls;

public partial class SplitDisabledAlert : UserControl
{
    // raised after the master switch was turned on here, so the page re-reads what it gates
    public event EventHandler? TurnedOn;

    public SplitDisabledAlert()
    {
        InitializeComponent();
        Refresh();
    }

    public void Refresh()
    {
        IsVisible = !VhApp.UserSettings.SplitTunneling.Enabled;
    }

    private async void OnTurnOnClick(object? sender, RoutedEventArgs e)
    {
        try {
            var settings = VhApp.UserSettings;
            settings.SplitTunneling.Enabled = true;
            await VhApp.SaveUserSettings(settings, CancellationToken.None);
            Refresh();
            TurnedOn?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }
}

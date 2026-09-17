using Avalonia.Controls;
using Avalonia.Interactivity;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Helpers;

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
        IsVisible = !AppModel.UserSettings.SplitTunneling.Enabled;
    }

    private async void OnTurnOnClick(object? sender, RoutedEventArgs e)
    {
        try {
            var settings = AppModel.UserSettings;
            settings.SplitTunneling.Enabled = true;
            await AppModel.SaveUserSettings(settings, CancellationToken.None);
            Refresh();
            TurnedOn?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }
}

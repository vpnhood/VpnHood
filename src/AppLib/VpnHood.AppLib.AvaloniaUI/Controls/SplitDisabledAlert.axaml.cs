using Avalonia.Controls;
using Avalonia.Interactivity;
using VpnHood.AppLib.AvaloniaUI.Helpers;

namespace VpnHood.AppLib.AvaloniaUI.Controls;

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
        IsVisible = !AppData.UserSettings.SplitTunneling.Enabled;
    }

    private async void OnTurnOnClick(object? sender, RoutedEventArgs e)
    {
        try {
            var settings = AppData.UserSettings;
            settings.SplitTunneling.Enabled = true;
            await AppData.SaveUserSettings(settings, CancellationToken.None);
            Refresh();
            TurnedOn?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }
}

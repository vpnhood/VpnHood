using Avalonia.Controls;
using Avalonia.Interactivity;

namespace VpnHood.AppLib.AvaloniaUI.Controls;

public partial class SplitDisabledAlert : UserControl
{
    private readonly VpnHoodApp _app = VpnHoodApp.Instance;

    // raised after the master switch was turned on here, so the page re-reads what it gates
    public event EventHandler? TurnedOn;

    public SplitDisabledAlert()
    {
        InitializeComponent();
        Refresh();
    }

    public void Refresh()
    {
        IsVisible = !_app.UserSettings.SplitTunneling.Enabled;
    }

    private void OnTurnOnClick(object? sender, RoutedEventArgs e)
    {
        _app.UserSettings.SplitTunneling.Enabled = true;
        _app.SettingsService.Save();
        Refresh();
        TurnedOn?.Invoke(this, EventArgs.Empty);
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using VpnHood.AppLib.Assets;
using VpnHood.AppUi.Hosting.Avalonia;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Views;

public partial class StarlinkToolsView : UserControl, IPage
{
    private readonly MainView _host;

    // Which relay to point at - none, one out on the internet, or one on this LAN.
    private enum RelayMode
    {
        Disabled,
        Remote,
        Local
    }

    private RelayMode _mode = RelayMode.Disabled;

    // the relay belongs to one server profile, whose name the page shows
    public StarlinkToolsView(MainView host, Guid clientProfileId)
    {
        _host = host;
        InitializeComponent();
        var s = Strings.Current;
        var profile = AppModel.FindClientProfileInfo(clientProfileId);
        ScopeText.Text = profile != null ? s.StarlinkToolsProfileScope(profile.ClientProfileName) : null;
        ScopeText.IsVisible = profile != null;

        DisabledRow.Title = s.StarlinkModeDisabled;
        DisabledRow.Description = s.StarlinkModeDisabledDesc;
        RemoteRow.Title = s.StarlinkModeRemote;
        RemoteRow.Description = s.StarlinkModeRemoteDesc;
        LocalRow.Title = s.StarlinkModeLocal;
        LocalRow.Description = s.StarlinkModeLocalDesc;
        AutoFindRow.Title = s.StarlinkAutoFind;
        Show();
    }

    private void Show()
    {
        DisabledRow.IsChecked = _mode == RelayMode.Disabled;
        RemoteRow.IsChecked = _mode == RelayMode.Remote;
        LocalRow.IsChecked = _mode == RelayMode.Local;
        RemoteBox.IsVisible = _mode == RelayMode.Remote;
        LocalPanel.IsVisible = _mode == RelayMode.Local;
    }

    public void FocusDefault()
    {
        DisabledRow.LandFocus();
    }

    private void OnDisabledClick(object? sender, EventArgs e)
    {
        _mode = RelayMode.Disabled;
        Show();
    }

    private void OnRemoteClick(object? sender, EventArgs e)
    {
        _mode = RelayMode.Remote;
        Show();
    }

    private void OnLocalClick(object? sender, EventArgs e)
    {
        _mode = RelayMode.Local;
        Show();
    }

    private void OnAutoFindClick(object? sender, EventArgs e)
    {
        AutoFindRow.IsChecked = !AutoFindRow.IsChecked;
    }

    private void OnFindClick(object? sender, RoutedEventArgs e)
    {
        _host.ShowSnackbar(Strings.Current.NotImplementedYet);
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using VpnHood.AppLib.AvaloniaUI.Helpers;
using VpnHood.AppLib.AvaloniaUI.ViewModels;

namespace VpnHood.AppLib.AvaloniaUI.Views;

public partial class HomeView : UserControl, IPage
{
    private readonly MainViewModel _viewModel;
    private readonly MainView _host;

    public HomeView(MainViewModel viewModel, MainView host)
    {
        _viewModel = viewModel;
        _host = host;
        DataContext = viewModel;
        InitializeComponent();
    }

    // Connect is where the input starts, as on the web UI's home.
    public void FocusDefault()
    {
        ConnectButton.LandFocus();
    }

    private async void OnConnectClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.ToggleConnect();
    }

    private void OnLocationClick(object? sender, RoutedEventArgs e)
    {
        _host.Navigate(new LocationsView(_viewModel, _host));
    }

    private void OnPhoneClick(object? sender, RoutedEventArgs e)
    {
        _host.Navigate(new PairingView(_host));
    }
}

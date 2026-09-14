using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using VpnHood.AppLib.AvaloniaUI.Helpers;
using VpnHood.AppLib.AvaloniaUI.Resources;
using VpnHood.AppLib.AvaloniaUI.ViewModels;

namespace VpnHood.AppLib.AvaloniaUI.Views;

public partial class AddServerView : UserControl, IPage
{
    private readonly MainViewModel _viewModel;
    private readonly MainView _host;

    public AddServerView(MainViewModel viewModel, MainView host)
    {
        _viewModel = viewModel;
        _host = host;
        InitializeComponent();
    }

    // the field, as the web UI's dialog opens with it focused
    public void FocusDefault()
    {
        KeyBox.LandFocus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Enter in the field adds, as it would submit that dialog's form
        if (e.Key == Key.Enter && KeyBox.IsFocused) {
            e.Handled = true;
            Add();
            return;
        }

        base.OnKeyDown(e);
    }

    private void OnAddClick(object? sender, RoutedEventArgs e)
    {
        Add();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        _host.GoBack();
    }

    // The key is the app's to judge: whatever it refuses is an unreadable key, and the page says so
    // in the web UI's own words rather than the exception's.
    private void Add()
    {
        var accessKey = KeyBox.Text?.Trim();
        if (string.IsNullOrEmpty(accessKey))
            return;

        try {
            var clientProfileId = _viewModel.AddAccessKey(accessKey);
            _host.GoBack();
            _ = _viewModel.ConnectToProfile(clientProfileId);
        }
        catch {
            ErrorText.Text = Strings.Current.InvalidAccessKeyFormat;
            ErrorText.IsVisible = true;
        }
    }
}

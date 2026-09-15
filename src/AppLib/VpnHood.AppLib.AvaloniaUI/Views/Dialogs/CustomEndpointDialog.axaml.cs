using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using VpnHood.AppLib.AvaloniaUI.Helpers;
using VpnHood.AppLib.AvaloniaUI.Resources;
using VpnHood.AppLib.ClientProfiles;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.AvaloniaUI.Views.Dialogs;

public partial class CustomEndpointDialog : DialogBase
{
    private readonly MainView _host;
    private readonly Guid _clientProfileId;

    public CustomEndpointDialog(MainView host, ClientProfileInfo profile)
    {
        _host = host;
        _clientProfileId = profile.ClientProfileId;
        InitializeComponent();
        EndpointBox.Watermark = Strings.Current.CustomEndpointPlaceHolder;
        EndpointBox.Text = profile.CustomServerEndpoints?.FirstOrDefault()?.ToString();
        EnabledSwitch.IsChecked = profile.IsCustomServerEndpointsEnabled;
    }

    public override void FocusDefault()
    {
        EndpointBox.LandFocus();
    }

    private void OnEnabledClick(object? sender, RoutedEventArgs e)
    {
        EnabledSwitch.IsChecked = EnabledSwitch.IsChecked != true;
    }

    private void OnEndpointChanged(object? sender, TextChangedEventArgs e)
    {
        ErrorText.IsVisible = false;
    }

    private async void OnEndpointKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await Save();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        await Save();
    }

    // the app judges the address; one it refuses is said so under the field, and the dialog stays
    private async Task Save()
    {
        _ = _host;
        var value = EndpointBox.Text?.Trim();
        try {
            await VpnHoodApp.Instance.UpdateClientProfile(_clientProfileId, new ClientProfileUpdateParams {
                CustomServerEndpoints = new Patch<string[]?>(string.IsNullOrEmpty(value) ? null : [value]),
                IsCustomServerEndpointsEnabled = new Patch<bool>(EnabledSwitch.IsChecked == true)
            }, CancellationToken.None);
            Close(true);
        }
        catch (Exception ex) {
            ErrorText.Text = string.IsNullOrEmpty(ex.Message) ? Strings.Current.CustomEndpointValidationError : ex.Message;
            ErrorText.IsVisible = true;
        }
    }
}

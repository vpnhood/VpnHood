using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using VpnHood.AppLib.AvaloniaUI.Helpers;
using VpnHood.AppLib.AvaloniaUI.Resources;
using VpnHood.AppLib.ClientProfiles;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.AvaloniaUI.Views.Dialogs;

public partial class PremiumCodeDialog : DialogBase
{
    private readonly MainView _host;
    private readonly VpnHoodApp _app = VpnHoodApp.Instance;

    public PremiumCodeDialog(MainView host)
    {
        _host = host;
        InitializeComponent();
    }

    public override void FocusDefault()
    {
        CodeBox.LandFocus();
    }

    private void OnCodeChanged(object? sender, TextChangedEventArgs e)
    {
        ErrorText.IsVisible = false;
        ActivateButton.IsEnabled = !string.IsNullOrWhiteSpace(CodeBox.Text);
    }

    private async void OnCodeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || !ActivateButton.IsEnabled)
            return;
        e.Handled = true;
        await Activate();
    }

    private async void OnActivateClick(object? sender, RoutedEventArgs e)
    {
        await Activate();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    // The profile write is the ONE door (keyring plan §7): it makes the code work here at once, and
    // when somebody is signed in the app hands it to the account by itself. Against a live
    // subscription the code is saved and queued, not used, and this says so.
    private async Task Activate()
    {
        var code = CodeBox.Text?.Trim() ?? "";
        var profileId = _app.State.ClientProfile?.ClientProfileId;
        if (profileId == null) {
            await _host.ShowError(Strings.Current.ProfileIdNotFoundDuringValidationMsg);
            return;
        }

        try {
            await _app.UpdateClientProfile(profileId.Value, new ClientProfileUpdateParams {
                AccessCode = new Patch<string?>(code)
            }, CancellationToken.None);
        }
        catch (Exception) {
            ErrorText.Text = Strings.Current.InvalidPremiumCodeNumbersMsg;
            ErrorText.IsVisible = true;
            return;
        }

        if (AppData.IsPremiumByAccount) {
            Close(true);
            await AppData.LoadAccount(true, CancellationToken.None);
            _host.ViewModel.Refresh();
            _host.ShowSnackbar(Strings.Current.PremiumCodeSavedForLaterMsg, SnackbarKind.Highlight, hasTimer: true);
            return;
        }

        // connect with the new code; a premium session confirms it
        Close(true);
        var pending = new PendingDialog();
        _ = _host.ShowDialog(pending);
        try {
            await _host.ViewModel.ConnectWith(new ConnectRequest(profileId.Value, null, IsPremium: true, ConnectPlanId.Normal, GoToHome: false));
            pending.Close();
            if (AppData.IsConnected() && AppData.IsPremiumUser)
                await _host.ShowDialog(new PremiumCodeCompleteDialog(_host));
        }
        finally {
            pending.Close();
        }
    }
}

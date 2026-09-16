using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using VpnHood.AppLib.Assets;
using VpnHood.AppLib.AvaloniaUI;
using VpnHood.AppLib.ClassicAvaloniaUi.Helpers;
using VpnHood.AppLib.Contracts.ClientProfiles;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.ClassicAvaloniaUi.Views.Dialogs;

public partial class PremiumCodeDialog : DialogBase
{
    private readonly MainView _host;

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
        try {
            if (e.Key != Key.Enter || !ActivateButton.IsEnabled)
                return;
            e.Handled = true;
            await Activate();
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnActivateClick(object? sender, RoutedEventArgs e)
    {
        try {
            await Activate();
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    // The profile write is the ONE door (keyring plan §7): it makes the code work here at once, and
    // when somebody is signed in the app hands it to the account by itself. Against a live
    // subscription the code is saved and queued, not used, and this says so.
    private async Task Activate()
    {
        var code = CodeBox.Text?.Trim() ?? "";
        var profileId = AppData.State.ClientProfile?.ClientProfileId;
        if (profileId == null) {
            await _host.ShowError(Strings.Current.ProfileIdNotFoundDuringValidationMsg);
            return;
        }

        try {
            await AppData.Api.ClientProfiles.Update(profileId.Value, new ClientProfileUpdateParams {
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
            await _host.ViewModel.ReloadConfig();
            _host.ShowSnackbar(Strings.Current.PremiumCodeSavedForLaterMsg);
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

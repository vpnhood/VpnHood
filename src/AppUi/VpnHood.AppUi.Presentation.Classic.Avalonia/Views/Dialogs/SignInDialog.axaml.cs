using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using VpnHood.AppUi.Common;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Helpers;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Resources;
using VpnHood.AppLib.Api.Accounts;
using VpnHood.Net.Toolkit.ApiClients;
using Avalonia;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Views.Dialogs;

public partial class SignInDialog : DialogBase
{
    private readonly MainView _host;
    private readonly string? _primaryProviderId = VhApp.PrimaryProviderId;
    private bool _isWorking;

    public SignInDialog(MainView host)
    {
        _host = host;
        InitializeComponent();
        var s = Strings.Current;

        // Google and Apple require their own mark and forbid recolouring; any other provider keeps
        // the app's button
        PrimaryButton.IsVisible = _primaryProviderId != null;
        PrimaryText.Text = PrimaryLabel();
        switch (_primaryProviderId?.ToLowerInvariant()) {
            case "google":
                Brand(BrandMarks.Google(), 48, Color.Parse("#FFFFFF"), Color.Parse("#1F1F1F"), Color.Parse("#747775"));
                break;
            case "apple":
                Brand(BrandMarks.Apple(Color.Parse("#000000")), 24, Color.Parse("#FFFFFF"), Color.Parse("#000000"), null);
                break;
            default:
                if (this.TryFindResource("HighlightBrush", out var value) && value is IBrush highlight)
                    PrimaryButton.Background = highlight;
                break;
        }

        // on a TV the email form is not offered - there is nothing to type on - and the phone stands
        // in for it; elsewhere the form is a step away
        var hasPassword = VhApp.HasPasswordSignIn;
        var isPhoneForEmail = VhApp.IsTvUi && hasPassword;
        OrRow.IsVisible = _primaryProviderId != null && hasPassword;
        PhoneButton.IsVisible = isPhoneForEmail;
        EmailButton.IsVisible = hasPassword && !isPhoneForEmail;
        EmailScopeHint.IsVisible = EmailButton.IsVisible;

        EmailBox.PlaceholderText = s.Email;
        PasswordBox.PlaceholderText = s.Password;
        CodeBox.PlaceholderText = s.TwoFactorCode;
        ProviderHint.IsVisible = _primaryProviderId != null;
        ProviderHint.Text = s.SignInProviderHint(PrimaryProviderName());
        // the one place the account website appears - where a browser can open it
        ForgotButton.IsVisible = MainView.IsExternalLinkUsable && VhApp.Features.AccountWebsiteUrl != null;
        BackButton.IsVisible = _primaryProviderId != null;

        // with no identity provider to choose, the dialog opens on the form itself
        if (_primaryProviderId == null)
            ShowStep(PasswordStep);
    }

    private void Brand(IEnumerable<Control> mark, double size, Color background, Color foreground, Color? border)
    {
        BrandCanvas.Width = size;
        BrandCanvas.Height = size;
        foreach (var shape in mark)
            BrandCanvas.Children.Add(shape);
        BrandMark.IsVisible = true;
        PrimaryButton.Background = new SolidColorBrush(background);
        PrimaryText.Foreground = new SolidColorBrush(foreground);
        if (border is { } borderColor) {
            PrimaryButton.BorderBrush = new SolidColorBrush(borderColor);
            PrimaryButton.BorderThickness = new Thickness(1);
        }
    }

    private string PrimaryLabel()
    {
        return _primaryProviderId?.ToLowerInvariant() switch {
            "google" => Strings.Current.SignInWithGoogle,
            "apple" => Strings.Current.SignInWithApple,
            _ => Strings.Current.SignIn
        };
    }

    // brand names are not translated; an unknown id is capitalized rather than left blank
    private string PrimaryProviderName()
    {
        var id = _primaryProviderId ?? "";
        return id.ToLowerInvariant() switch {
            "apple" => "Apple",
            "google" => "Google",
            "microsoft" => "Microsoft",
            "" => "",
            _ => char.ToUpperInvariant(id[0]) + id[1..]
        };
    }

    public override void FocusDefault()
    {
        if (StartStep.IsVisible) {
            if (PrimaryButton.IsVisible) PrimaryButton.LandFocus();
            else if (PhoneButton.IsVisible) PhoneButton.LandFocus();
            else if (EmailButton.IsVisible) EmailButton.LandFocus();
            else StartCancelButton.LandFocus();
        }
        else if (PasswordStep.IsVisible) EmailBox.LandFocus();
        else if (ChallengeStep.IsVisible) CodeBox.LandFocus();
        else SavedButton.LandFocus();
    }

    private void ShowStep(Control step)
    {
        StartStep.IsVisible = step == StartStep;
        PasswordStep.IsVisible = step == PasswordStep;
        ChallengeStep.IsVisible = step == ChallengeStep;
        BackupStep.IsVisible = step == BackupStep;
        PasswordError.IsVisible = false;
        CodeError.IsVisible = false;
        FocusDefault();
    }

    private async void OnPrimaryClick(object? sender, RoutedEventArgs e)
    {
        try {
            Close();
            try {
                await _host.ViewModel.SignIn();
            }
            catch (Exception ex) {
                await _host.ProcessError(ex);
            }
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    // the phone is the TV's keyboard: the pairing page takes this dialog's place
    private void OnPhoneClick(object? sender, RoutedEventArgs e)
    {
        Close();
        _host.Navigate(new PairingView(_host, Strings.Current.RemoteAccessHintSignIn));
    }

    private void OnEmailClick(object? sender, RoutedEventArgs e)
    {
        ShowStep(PasswordStep);
    }

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        ShowStep(StartStep);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnSavedClick(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void OnFieldChanged(object? sender, TextChangedEventArgs e)
    {
        SubmitPasswordButton.IsEnabled = !_isWorking && !string.IsNullOrWhiteSpace(EmailBox.Text) && !string.IsNullOrEmpty(PasswordBox.Text);
        SubmitCodeButton.IsEnabled = !_isWorking && !string.IsNullOrWhiteSpace(CodeBox.Text);
    }

    private async void OnPasswordKeyDown(object? sender, KeyEventArgs e)
    {
        try {
            if (e.Key != Key.Enter || !SubmitPasswordButton.IsEnabled) return;
            e.Handled = true;
            await SubmitPassword();
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnCodeKeyDown(object? sender, KeyEventArgs e)
    {
        try {
            if (e.Key != Key.Enter || !SubmitCodeButton.IsEnabled) return;
            e.Handled = true;
            await SubmitChallenge();
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnSubmitPasswordClick(object? sender, RoutedEventArgs e)
    {
        try {
            await SubmitPassword();
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnSubmitCodeClick(object? sender, RoutedEventArgs e)
    {
        try {
            await SubmitChallenge();
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnForgotClick(object? sender, RoutedEventArgs e)
    {
        try {
            if (VhApp.Features.AccountWebsiteUrl is { } url)
                await _host.OpenLink(url, Strings.Current.ForgotPassword);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async Task SubmitPassword()
    {
        _isWorking = true;
        SubmitPasswordButton.IsEnabled = false;
        try {
            var result = await _host.ViewModel.SignInWithPassword(EmailBox.Text?.Trim() ?? "", PasswordBox.Text ?? "");
            if (result.State != SignInState.SignedIn) {
                // the password is right; nothing is signed in until the second factor answers
                CodeBox.Text = "";
                ShowStep(ChallengeStep);
                return;
            }
            Close(true);
        }
        catch (Exception ex) {
            PasswordError.Text = MessageFor(ex);
            PasswordError.IsVisible = true;
        }
        finally {
            _isWorking = false;
            OnFieldChanged(null, null!);
        }
    }

    private async Task SubmitChallenge()
    {
        _isWorking = true;
        SubmitCodeButton.IsEnabled = false;
        try {
            var result = await _host.ViewModel.CompleteSignInChallenge(CodeBox.Text?.Trim() ?? "");
            if (result.NewBackupCode is { } backupCode) {
                // the backup code was spent and rotated - the only time the new one is shown
                BackupCodeText.Text = backupCode;
                ShowStep(BackupStep);
                return;
            }
            Close(true);
        }
        catch (Exception ex) {
            CodeError.Text = MessageFor(ex);
            CodeError.IsVisible = true;
            // a spent or expired challenge restarts from the password, with the message saying why
            if (CodeOf(ex) == "invalid_challenge") {
                var message = CodeError.Text;
                ShowStep(PasswordStep);
                PasswordError.Text = message;
                PasswordError.IsVisible = true;
            }
        }
        finally {
            _isWorking = false;
            OnFieldChanged(null, null!);
        }
    }

    // the portal's machine code is the contract; its English detail the fallback for a code this
    // build does not know
    private static string? CodeOf(Exception ex)
    {
        return ex.ToApiError().Data.GetValueOrDefault("Code");
    }

    private string MessageFor(Exception ex)
    {
        var s = Strings.Current;
        return CodeOf(ex) switch {
            "invalid_credentials" => s.SignInInvalidCredentials,
            "too_many_attempts" => s.SignInCooldown,
            "invalid_code" => s.SignInWrongCode,
            "invalid_challenge" => s.SignInChallengeExpired,
            "unsupported_two_factor" => s.SignInUnsupportedTwoFactor,
            "no_account" => _primaryProviderId != null ? s.SignInNoAccountWithProvider(PrimaryProviderName()) : s.SignInNoAccount,
            "account_ambiguous" => s.SignInAccountAmbiguous,
            _ => ex.Message
        };
    }
}

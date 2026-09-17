using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using VpnHood.AppLib.Assets;
using VpnHood.AppLib.AvaloniaUI;
using VpnHood.AppLib.ClassicAvaloniaUi.Helpers;
using VpnHood.AppLib.ClassicAvaloniaUi.Views.Dialogs;
using VpnHood.AppLib.Contracts.Accounts;
using VpnHood.AppLib.Contracts.App;
using VpnHood.AppLib.Contracts.ClientProfiles;
using VpnHood.AppLib.Contracts.Sessions;

namespace VpnHood.AppLib.ClassicAvaloniaUi.Views;

public partial class AccountView : UserControl, IPage
{
    // the same periods the purchase page names (GooglePlayBillingSubscriptionPeriods)
    private const string OneMonth = "P1M";
    private const string SixMonths = "P6M";
    private const string OneYear = "P1Y";

    private readonly MainView _host;
    private string? _premiumCode;
    private bool _isCodeRevealed;
    private TextBlock? _codeText;

    public AccountView(MainView host)
    {
        _host = host;
        InitializeComponent();
        Fill();
        // THE refresh: the app never polls the account; opening this page is the person asking
        _ = Refresh();
    }

    private async Task Refresh()
    {
        try {
            await AppModel.LoadAccount(true, CancellationToken.None);
            _host.ViewModel.Refresh();
            Fill();
        }
        catch (Exception ex) {
            await _host.ProcessError(ex);
        }
    }

    private void Fill()
    {
        var s = Strings.Current;
        var state = AppModel.State;
        var account = AppModel.Account;
        var profile = state.ClientProfile;
        var isPremiumUser = AppModel.IsPremiumUser;
        var isPremiumByAccount = AppModel.IsPremiumByAccount;
        var hasCode = profile?.HasAccessCode == true;

        Sheet.Classes.Set("grad-sheet", isPremiumUser);
        PremiumImage.IsVisible = isPremiumUser;

        UserCard.IsVisible = account != null;
        UserName.Text = account?.Name;
        UserName.IsVisible = !string.IsNullOrEmpty(account?.Name);
        UserEmail.Text = account?.Email;
        SignOutButton.Classes.Set("active", isPremiumUser);

        RefusalAlert.IsVisible = !isPremiumByAccount && hasCode && profile?.AccessCodeRefusal != null;
        RefusalText.Text = RefusalNotice(profile?.AccessCodeRefusal);

        SignOutForCodeCard.IsVisible = isPremiumByAccount && AppModel.CanImportAccessCode;
        ChangeCodeCard.IsVisible = !isPremiumByAccount && hasCode && AppModel.CanImportAccessCode;
        RemoveCodeCard.IsVisible = !isPremiumByAccount && hasCode && account == null;
        SubscriptionCard.IsVisible = isPremiumByAccount;
        CodeCard.IsVisible = isPremiumByAccount || hasCode;
        UpgradeCard.IsVisible = !isPremiumByAccount && !hasCode && profile?.CanGoPremium == true;
        DeleteCard.IsVisible = account != null;

        if (isPremiumByAccount)
            FillSubscription(account?.Subscription);
        if (CodeCard.IsVisible)
            FillCode(state.SessionInfo?.AccessInfo);
        RichText.Apply(UpgradeText, s.UpgradeAccountDesc);
    }

    // say "expired" only when the server said AccessExpired; a generic rejection is a rejection
    private static string RefusalNotice(AccessCodeRefusal? refusal)
    {
        if (refusal == null)
            return "";
        var date = Format.ShortDate(refusal.RefusedTime);
        return refusal.ErrorCode == SessionErrorCode.AccessExpired
            ? Strings.Current.CodeRefusedExpiredNotice(date)
            : Strings.Current.CodeRefusedRejectedNotice(date);
    }

    // SubscriptionDetails.vue: the dates, the renewal, the price with its period
    private void FillSubscription(Subscription? subscription)
    {
        var s = Strings.Current;
        SubscriptionRows.Children.Clear();
        var isAutoRenew = subscription?.IsAutoRenew == true;
        if (subscription?.CreatedTime is { } created)
            AddRow(SubscriptionRows, s.SubscribedSince, Format.ShortDate(created), null);
        AddRow(SubscriptionRows, isAutoRenew ? s.NextPayment : s.ExpirationTime, Format.ShortDate(subscription?.ExpirationTime), isAutoRenew ? "active" : "error");
        AddRow(SubscriptionRows, s.AutoRenew, isAutoRenew ? s.Yes : s.No, isAutoRenew ? "active" : "error");
        if (subscription?.PriceAmount is { } price)
            AddRow(SubscriptionRows, s.Price, $"{subscription.PriceCurrency} {price}{PricePeriod(subscription.BillingPeriod)}", null);

        // three answers: our store can show its screen here; our store, but not on this device;
        // another store billed it, which is not named
        var management = subscription?.Management;
        ManageButton.IsVisible = management == SubscriptionManagement.Available;
        ManageNote.IsVisible = management != SubscriptionManagement.Available;
        ManageNote.Text = management == SubscriptionManagement.NotOnThisDevice ? s.SubscriptionManageOnAnotherDevice : s.SubscriptionManagedWherePurchased;
    }

    private static string PricePeriod(string? period)
    {
        return period switch {
            OneMonth => Strings.Current.PerMonth,
            SixMonths => Strings.Current.Per6Months,
            OneYear => Strings.Current.PerYear,
            _ => ""
        };
    }

    // PremiumCodeDetails.vue: the code behind the eye, and the access it gives
    private void FillCode(AccessInfo? access)
    {
        var s = Strings.Current;
        CodeRows.Children.Clear();
        var canShowCode = AppModel.CanViewAccessCode && AppModel.State.ClientProfile?.HasAccessCode == true;
        if (canShowCode)
            AddCodeRow();

        if (access != null) {
            AddRow(CodeRows, s.MaxDevice, access.MaxDeviceCount > 0 ? access.MaxDeviceCount.ToString() : s.Unlimited, "active");
            var summary = access.DevicesSummary;
            AddRow(CodeRows, s.UsedDevice, summary?.HasMoreDevices == true ? s.MoreThanXDevices(summary.DeviceCount) : (summary?.DeviceCount ?? 0).ToString(), "highlight");
            AddRow(CodeRows, s.ActivatedOn, Format.ShortDate(access.CreatedTime), "active");
            AddRow(CodeRows, s.ExpirationDate, access.ExpirationTime is { } expire ? Format.ShortDate(expire) : s.Never, access.ExpirationTime != null ? "error" : "active");
            AddRow(CodeRows, s.LastUsed, Format.ShortDate(access.LastUsedTime), "highlight");
        }
        CodeHint.IsVisible = canShowCode;
        CodeNotice.IsVisible = access == null;
        MoreDetailsButton.IsVisible = access != null;
    }

    private const string MaskedCode = "••••-••••-••••-••••-••••";

    private void AddCodeRow()
    {
        var s = Strings.Current;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 4) };
        var label = new TextBlock { Text = $"{s.Code}:", VerticalAlignment = VerticalAlignment.Center };
        label.Classes.Add("label-large");
        label.Classes.Add("disabled");
        row.Children.Add(label);

        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        _codeText = new TextBlock { Text = MaskedCode, VerticalAlignment = VerticalAlignment.Center };
        _codeText.Classes.Add("label-large");
        _codeText.Classes.Add("active");
        line.Children.Add(_codeText);

        var eye = new Button { Content = new TextBlock { Text = Mdi.EyeOutline, FontSize = 18 } };
        eye.Classes.Add("icon");
        eye.Classes.Add("small");
        ((TextBlock)eye.Content).Classes.Add("mdi");
        ((TextBlock)eye.Content).Classes.Add("disabled");
        eye.Click += async (_, _) => {
            try {
                await ToggleReveal(eye);
            }
            catch (Exception ex) {
                await this.ReportError(ex);
            }
        };
        line.Children.Add(eye);

        var copy = new Button { Content = new TextBlock { Text = Mdi.ContentCopy, FontSize = 18 } };
        copy.Classes.Add("icon");
        copy.Classes.Add("small");
        ((TextBlock)copy.Content).Classes.Add("mdi");
        ((TextBlock)copy.Content).Classes.Add("disabled");
        copy.Click += async (_, _) => {
            try {
                await CopyCode(copy);
            }
            catch (Exception ex) {
                await this.ReportError(ex);
            }
        };
        line.Children.Add(copy);

        Grid.SetColumn(line, 1);
        row.Children.Add(line);
        CodeRows.Children.Add(row);
    }

    // fetched on demand, never on arrival: the raw code leaves the app only when asked for
    private async Task<string?> LoadPremiumCode()
    {
        if (_premiumCode != null)
            return _premiumCode;

        var profileId = AppModel.ClientProfileId;
        if (profileId == null) {
            _premiumCode = Strings.Current.CouldNotGetClientProfileId;
            return null;
        }

        var code = await AppModel.Api.ClientProfiles.GetAccessCode(profileId.Value, CancellationToken.None);
        _premiumCode = string.IsNullOrEmpty(code) ? Strings.Current.CouldNotGetPremiumCode : Format.CodeGroups(code);
        return _premiumCode;
    }

    private async Task ToggleReveal(Button eye)
    {
        if (!_isCodeRevealed)
            await LoadPremiumCode();
        _isCodeRevealed = !_isCodeRevealed;
        if (_codeText != null)
            _codeText.Text = _isCodeRevealed ? _premiumCode ?? MaskedCode : MaskedCode;
        if (eye.Content is TextBlock glyph)
            glyph.Text = _isCodeRevealed ? Mdi.EyeOffOutline : Mdi.EyeOutline;
    }

    private async Task CopyCode(Button copy)
    {
        var code = await LoadPremiumCode();
        if (code == null)
            return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null)
            return;
        await clipboard.SetTextAsync(code);
        if (copy.Content is not TextBlock glyph)
            return;
        glyph.Text = Mdi.Check;
        glyph.Classes.Set("active", true);
        await Task.Delay(2000);
        glyph.Text = Mdi.ContentCopy;
        glyph.Classes.Set("active", false);
    }

    // a label and a value, zebra-striped as the web UI's lists are
    private static void AddRow(StackPanel rows, string label, string value, string? valueClass)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var labelText = new TextBlock { Text = $"{label}:", VerticalAlignment = VerticalAlignment.Center };
        labelText.Classes.Add("label-large");
        labelText.Classes.Add("disabled");
        var valueText = new TextBlock { Text = value, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        valueText.Classes.Add("label-large");
        if (valueClass != null)
            valueText.Classes.Add(valueClass);
        Grid.SetColumn(valueText, 1);
        row.Children.Add(labelText);
        row.Children.Add(valueText);
        var stripe = new Border { Child = row, Padding = new Thickness(7, 5), CornerRadius = new CornerRadius(4) };
        if (rows.Children.Count % 2 == 0 && rows.TryFindResource("ZebraOnConfigCardBgBrush", out var brush) && brush is IBrush zebra)
            stripe.Background = zebra;
        rows.Children.Add(stripe);
    }

    public void FocusDefault()
    {
        var first = this.FindFirstButton();
        if (first != null) first.LandFocus();
        else Header.FocusBack();
    }

    private async void OnSignOutClick(object? sender, RoutedEventArgs e)
    {
        try {
            await _host.ViewModel.SignOut();
        }
        catch (Exception ex) {
            await _host.ProcessError(ex);
        }
    }

    private async void OnChangeCodeClick(object? sender, RoutedEventArgs e)
    {
        try {
            await _host.ShowDialog(new PremiumCodeDialog(_host));
            Fill();
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    // signed out only: the device's copy is the only one there is
    private async void OnRemoveCodeClick(object? sender, RoutedEventArgs e)
    {
        try {
            if (!await _host.Confirm(Strings.Current.ConfirmRemovePremiumCode, Strings.Current.ConfirmRemovePremiumCodeDesc))
                return;
            _host.GoHome();
            await _host.ViewModel.RemovePremiumCode();
        }
        catch (Exception ex) {
            await _host.ProcessError(ex);
        }
    }

    // the store shows its own screen; this only asks, and a device that cannot fails loudly
    private async void OnManageClick(object? sender, RoutedEventArgs e)
    {
        try {
            if (!AppModel.Features.IsBillingSupported)
                return;
            ManageButton.IsEnabled = false;
            try {
                await AppModel.Api.Billing.OpenSubscriptionManagement(CancellationToken.None);
            }
            catch (Exception ex) {
                await _host.ProcessError(ex);
            }
            finally {
                ManageButton.IsEnabled = true;
            }
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private void OnMoreDetailsClick(object? sender, RoutedEventArgs e)
    {
        _host.Navigate(new StatisticsView(_host));
    }

    private void OnGoPremiumClick(object? sender, RoutedEventArgs e)
    {
        _host.Navigate(new PurchaseSubscriptionView(_host, null));
    }

    private async void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        try {
            if (!await _host.ShowDialog(new DeleteAccountDialog()))
                return;
            await _host.ViewModel.DeleteAccount();
        }
        catch (Exception ex) {
            await _host.ProcessError(ex);
        }
    }
}

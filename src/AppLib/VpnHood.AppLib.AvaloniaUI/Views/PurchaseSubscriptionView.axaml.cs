using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using VpnHood.AppLib.Abstractions.Billing;
using VpnHood.AppLib.Assets;
using VpnHood.AppLib.AvaloniaUI.Helpers;
using VpnHood.AppLib.AvaloniaUI.Views.Dialogs;
using VpnHood.Core.Toolkit.ApiClients;

namespace VpnHood.AppLib.AvaloniaUI.Views;

public partial class PurchaseSubscriptionView : UserControl, IPage
{
    // the store's periods (GooglePlayBillingSubscriptionPeriods)
    private const string OneMonth = "P1M";
    private const string SixMonths = "P6M";
    private const string OneYear = "P1Y";

    private readonly MainView _host;
    private readonly Guid? _clientProfileId;
    private readonly List<(SubscriptionPlan Plan, Button Button)> _planButtons = [];
    private AppPurchaseOptions? _options;
    private SubscriptionPlan? _selectedPlan;
    private double _basePrice;

    public PurchaseSubscriptionView(MainView host, Guid? clientProfileId)
    {
        _host = host;
        _clientProfileId = clientProfileId;
        InitializeComponent();
        BackButton.IsVisible = !AppData.IsTvUi;
        TermsLink.IsVisible = AppData.Features.TermsOfUseUrl != null;
        PrivacyLink.IsVisible = AppData.Features.PrivacyPolicyUrl != null;
        LinksDot.IsVisible = TermsLink.IsVisible && PrivacyLink.IsVisible;
        LinksRow.IsVisible = TermsLink.IsVisible || PrivacyLink.IsVisible;
        _ = LoadOptions();
    }

    public void FocusDefault()
    {
        if (PurchaseButton.IsVisible && StorePanel.IsVisible) _planButtons.FirstOrDefault(x => x.Plan == _selectedPlan).Button?.LandFocus();
        else if (RetryButton.IsVisible && StoreErrorCard.IsVisible) RetryButton.LandFocus();
        else if (WebButton.IsVisible) WebButton.LandFocus();
        else if (CodeButton.IsVisible) CodeButton.LandFocus();
        else if (RestoreButton.IsVisible) RestoreButton.LandFocus();
        else if (BackButton.IsVisible) BackButton.LandFocus();
        else Carousel.FirstButton.LandFocus();
    }

    // The catalog comes from the portal and nothing stands in for it: a failed load is only
    // recoverable by loading again, which the store-unavailable card offers.
    private async Task LoadOptions()
    {
        LoadingPanel.IsVisible = true;
        OptionsPanel.IsVisible = false;
        try {
            var profileId = _clientProfileId ?? AppData.ClientProfileId ?? throw new InvalidOperationException("Client profile id is required.");
            _options = await AppData.Api.ClientProfiles.GetPurchaseOptions(profileId, CancellationToken.None);
            ShowOptions(_options);
        }
        catch (Exception ex) {
            await _host.ProcessError(ex);
        }
        finally {
            LoadingPanel.IsVisible = false;
        }
    }

    private void ShowOptions(AppPurchaseOptions options)
    {
        OptionsPanel.IsVisible = true;
        StorePanel.IsVisible = options.IsStoreAvailable;
        StoreErrorCard.IsVisible = options is { IsStoreAvailable: false, StoreError: not null };
        WebButton.IsVisible = options.PurchaseUrl != null && MainView.IsExternalLinkUsable;
        CodeButton.IsVisible = options.CanGoPremiumByCode;
        RestoreButton.IsVisible = AppData.Features.IsAccountSupported;
        RestoreText.Text = AppData.Account != null ? Strings.Current.RestorePurchase : Strings.Current.AlreadyPremium;
        if (options.IsStoreAvailable)
            ShowPlans(options.SubscriptionPlans);
        FocusDefault();
    }

    // PurchaseByStore: the monthly plan is the base the others are discounted against
    private void ShowPlans(IReadOnlyList<SubscriptionPlan> plans)
    {
        var oneMonth = plans.FirstOrDefault(x => x.Period == OneMonth);
        _basePrice = oneMonth?.BasePrice ?? 0;
        _selectedPlan = oneMonth ?? plans.FirstOrDefault();

        Plans.Children.Clear();
        _planButtons.Clear();
        foreach (var plan in plans) {
            var button = BuildPlanButton(plan);
            _planButtons.Add((plan, button));
            Plans.Children.Add(button);
        }
        ShowSelection();
    }

    private Button BuildPlanButton(SubscriptionPlan plan)
    {
        var s = Strings.Current;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { Text = PlanTitle(plan.Period), VerticalAlignment = VerticalAlignment.Center };
        title.Classes.Add("body-medium");
        titleRow.Children.Add(title);
        var discount = DiscountPercent(plan);
        if (discount > 0) {
            var chip = new Border { Child = new TextBlock { Text = $"-{discount}%" } };
            chip.Classes.Add("chip");
            chip.Classes.Add("flat-enable-premium");
            chip.Classes.Add("comfortable");
            titleRow.Children.Add(chip);
        }
        row.Children.Add(titleRow);

        var priceColumn = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, Spacing = 2 };
        if (discount > 0) {
            var basePrice = new TextBlock {
                Text = Format.Price(plan.CurrencySymbol, BasePriceOf(plan.Period)),
                TextDecorations = TextDecorations.Strikethrough,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            basePrice.Classes.Add("body-small");
            basePrice.Classes.Add("disabled");
            priceColumn.Children.Add(basePrice);
        }
        var priceRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right };
        var price = new TextBlock { Text = Format.Price(plan.CurrencySymbol, plan.CurrentPrice), VerticalAlignment = VerticalAlignment.Center };
        price.Classes.Add("body-large");
        var period = new TextBlock { Text = PricePeriod(plan.Period), VerticalAlignment = VerticalAlignment.Center };
        period.Classes.Add("body-small");
        period.Classes.Add("disabled");
        priceRow.Children.Add(price);
        priceRow.Children.Add(period);
        priceColumn.Children.Add(priceRow);
        Grid.SetColumn(priceColumn, 1);
        row.Children.Add(priceColumn);

        var button = new Button { Content = row };
        button.Classes.Add("plan");
        button.Click += (_, _) => {
            _selectedPlan = plan;
            ShowSelection();
        };
        _ = s;
        return button;
    }

    private void ShowSelection()
    {
        foreach (var (plan, button) in _planButtons)
            button.Classes.Set("selected", plan == _selectedPlan);
        if (_selectedPlan is { } selected)
            AutoRenewText.Text = $"{Strings.Current.AutoRenewAt} {Format.Price(selected.CurrencySymbol, selected.BasePrice)}{PricePeriod(selected.Period)}";
        PurchaseButton.IsEnabled = _selectedPlan != null;
    }

    private static string PlanTitle(string period)
    {
        return period switch {
            OneMonth => Strings.Current._1Month,
            SixMonths => Strings.Current._6Months,
            OneYear => Strings.Current._1Year,
            _ => Strings.Current.UnknownError
        };
    }

    private static string PricePeriod(string period)
    {
        return period switch {
            OneMonth => Strings.Current.PerMonth,
            SixMonths => Strings.Current.Per6Months,
            OneYear => Strings.Current.PerYear,
            _ => Strings.Current.UnknownError
        };
    }

    private double BasePriceOf(string period)
    {
        return period switch {
            SixMonths => _basePrice * 6,
            OneYear => _basePrice * 12,
            _ => _basePrice
        };
    }

    // a plan priced above the monthly base shows no discount, and the monthly base none of its own
    private int DiscountPercent(SubscriptionPlan plan)
    {
        if (_basePrice <= 0 || (plan.Period == OneMonth && plan.CurrentPrice.Equals(_basePrice)))
            return 0;
        var basePrice = BasePriceOf(plan.Period);
        return Math.Max(0, (int)Math.Round((basePrice - plan.CurrentPrice) / basePrice * 100));
    }

    // ---- the store purchase (PurchaseByStore.vue) ----

    // Is the signed-in account already served - a store subscription, or the code the backend chose
    // for it? Asked after signing in, before the store's sheet: after it the money has moved.
    private static bool IsAccountServed()
    {
        var account = AppData.Account;
        return account?.Subscription != null || account?.AccessCodeInfo != null;
    }

    private async void OnPurchaseClick(object? sender, RoutedEventArgs e)
    {
        try {
            if (_selectedPlan is not { } plan)
                return;

            try {
                if (AppData.Account == null) {
                    await _host.ViewModel.SignIn(onPurchase: true);
                    if (IsAccountServed()) {
                        _host.Replace(new AccountView(_host));
                        await _host.ShowError(Strings.Current.HaveActiveSubscription);
                        return;
                    }
                }
                await Purchase(new PurchaseParams { PlanToken = plan.PlanToken });
            }
            catch (Exception ex) {
                await _host.ProcessError(ex);
            }
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async Task Purchase(PurchaseParams purchaseParams)
    {
        if (!AppData.Features.IsBillingSupported)
            throw new InvalidOperationException("Billing service is not available.");
        var pending = new PendingDialog();
        _ = _host.ShowDialog(pending);
        try {
            await AppData.Api.Billing.Purchase(purchaseParams, CancellationToken.None);
            await AppData.LoadAccount(false, CancellationToken.None);
            await _host.ViewModel.ReloadConfig();
            pending.Close();

            // Congratulate only what the refreshed account confirms: the store can answer with a
            // stale transaction whose entitlement has since expired.
            if (IsAccountServed())
                await _host.ShowDialog(new PurchaseCompleteDialog(_host));
            else
                await _host.ShowError(Strings.Current.RestoredPurchaseExpiredMsg);
        }
        catch (Exception ex) when (ex.ToApiError().TypeName == "AlreadyExistsException") {
            pending.Close();
            _host.Replace(new AccountView(_host));
            await _host.ShowError(Strings.Current.HaveActiveSubscription);
        }
        finally {
            pending.Close();
        }
    }

    // ---- restoring a premium already owned (RestorePremium.vue) ----

    private async void OnRestoreClick(object? sender, RoutedEventArgs e)
    {
        try {
            if (AppData.Account == null && AppData.HasSignInChoice) {
                // the dialog owns the sign-in; the restore goes on once there is an account
                await _host.ShowDialog(new SignInDialog(_host));
                if (AppData.Account == null)
                    return;
            }
            await Restore();
        }
        catch (Exception ex) {
            await _host.ProcessError(ex);
        }
    }

    private async Task Restore()
    {
        var pending = new PendingDialog();
        _ = _host.ShowDialog(pending);
        try {
            if (AppData.Account == null) {
                try {
                    await _host.ViewModel.SignIn(onPurchase: true);
                }
                catch (Exception ex) when (ex.ToApiError().TypeName == "NoCredentialException") {
                    throw new Exception(Strings.Current.GooglePlayLoginNoCredentialError);
                }
            }

            // a build with no store has nothing to ask; signing in and reading the account is the
            // whole of the restore there
            var restored = AppData.Features.IsBillingSupported
                && await AppData.Api.Billing.RestorePurchase(CancellationToken.None);
            await AppData.LoadAccount(false, CancellationToken.None);
            await _host.ViewModel.ReloadConfig();

            // "am I premium again?" is the question, and the refreshed account the only witness;
            // the wait is over before the answer is shown
            var isServed = IsAccountServed();
            pending.Close();
            if (isServed)
                await _host.ShowDialog(new PurchaseCompleteDialog(_host));
            else if (restored)
                await _host.ShowError(Strings.Current.RestoredPurchaseExpiredMsg);
            else
                await _host.ShowError(Strings.Current.NoPurchaseToRestore);
        }
        finally {
            pending.Close();
        }
    }

    // ---- the rest ----

    private async void OnRetryClick(object? sender, RoutedEventArgs e)
    {
        try {
            await LoadOptions();
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnMoreInfoClick(object? sender, RoutedEventArgs e)
    {
        try {
            if (_options?.StoreError is { } error)
                await _host.ShowError(error.Message);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnWebClick(object? sender, RoutedEventArgs e)
    {
        try {
            if (_options?.PurchaseUrl is { } url)
                await _host.OpenLink(url, Strings.Current.PurchaseViaWeb);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnCodeClick(object? sender, RoutedEventArgs e)
    {
        try {
            await _host.ShowDialog(new PremiumCodeDialog(_host));
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnTermsClick(object? sender, RoutedEventArgs e)
    {
        try {
            if (AppData.Features.TermsOfUseUrl is { } url)
                await _host.OpenLink(url, Strings.Current.TermsOfUse);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnPrivacyClick(object? sender, RoutedEventArgs e)
    {
        try {
            if (AppData.Features.PrivacyPolicyUrl is { } url)
                await _host.OpenLink(url, Strings.Current.PrivacyPolicy);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        _host.GoBack();
    }
}

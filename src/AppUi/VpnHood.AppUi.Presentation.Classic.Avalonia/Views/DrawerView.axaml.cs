using Avalonia.Controls;
using Avalonia.Interactivity;
using VpnHood.AppUi.Common;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Helpers;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Resources;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Views.Dialogs;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Views;

public partial class DrawerView : UserControl
{
    private const string ChangelogUrl = "https://github.com/vpnhood/VpnHood/blob/main/CHANGELOG.md";
    private const string PersonalServerUrl = "https://github.com/vpnhood/VpnHood/wiki/VpnHood-Manager";
    private const string WebsiteUrl = "https://www.vpnhood.com/";
    private const string EngineUrl = "https://github.com/vpnhood/VpnHood";
    private const string LinkedInUrl = "https://www.linkedin.com/company/vpnhood";
    private const string InstagramUrl = "https://www.instagram.com/vpnhood/";
    private const string XUrl = "https://x.com/vpnhood";

    private readonly MainView _host;

    public DrawerView(MainView host)
    {
        _host = host;
        InitializeComponent();

        var features = VhApp.Features;
        var state = VhApp.State;
        // the product's logo, by the store path the head named; not the look's to choose
        AppImage.SetSource(Logo, features.LogoAssetPath);
        AppNameText.Text = features.AppName;
        // app.major.minor.build; the web UI adds its own bundle's build as a fourth segment, which
        // this UI has none of
        VersionText.Text = features.Version.ToString(3);

        PremiumItem.IsVisible = !VhApp.IsPremiumUser && VhApp.IsPremiumSupported && VhApp.CanGoPremium;
        AccountItem.IsVisible = features.IsAccountSupported;
        var account = VhApp.Account;
        AccountTitle.Text = account != null ? Strings.Current.Account : SignInLabel();
        AccountEmail.Text = account?.Email;
        AccountEmail.IsVisible = account?.Email != null;
        DiagnoseItem.IsEnabled = state.CanDiagnose;
        // the updater is the capability signal: null exactly when no updater was configured
        UpdateItem.IsVisible = state.UpdaterStatus != null;
        PersonalServerItem.IsVisible = features.IsAddAccessKeySupported; // one you add the key of
        PrivacyItem.IsVisible = features.PrivacyPolicyUrl != null;
    }

    // "Sign in with Google" for the one store method, plain "Sign in" with a chooser or none
    private static string SignInLabel()
    {
        var providerId = VhApp.PrimaryProviderId;
        if (providerId == null || VhApp.HasSignInChoice)
            return Strings.Current.SignIn;
        return providerId.ToLowerInvariant() switch {
            "google" => Strings.Current.SignInWithGoogle,
            "apple" => Strings.Current.SignInWithApple,
            _ => Strings.Current.SignIn
        };
    }

    public void FocusDefault()
    {
        var first = Items.Children.OfType<Button>().FirstOrDefault(x => x is { IsVisible: true, IsEnabled: true });
        first?.LandFocus();
    }

    private void OnPremiumClick(object? sender, RoutedEventArgs e)
    {
        _host.CloseDrawer();
        _host.Replace(new PurchaseSubscriptionView(_host, null));
    }

    private async void OnAccountClick(object? sender, RoutedEventArgs e)
    {
        try {
            _host.CloseDrawer();
            if (VhApp.Account != null) {
                _host.Replace(new AccountView(_host));
                return;
            }

            // The same rule the home and the paywall follow: where there is a choice of methods -
            // or only the portal's password, which is a choice of one - the dialog owns the sign
            // in. ViewModel.SignIn is the store's own method, and asking it of a build that has
            // none only ever produced "This build reports no sign-in method".
            if (VhApp.HasSignInChoice) {
                await _host.ShowDialog(new SignInDialog(_host));
                return;
            }

            await _host.ViewModel.SignIn();
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        _host.CloseDrawer();
        _host.Replace(new SettingsView(_host));
    }

    private async void OnDiagnoseClick(object? sender, RoutedEventArgs e)
    {
        try {
            _host.CloseDrawer();
            await _host.ViewModel.Diagnose();
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnUpdateClick(object? sender, RoutedEventArgs e)
    {
        try {
            UpdateIcon.Text = Mdi.Loading;
            UpdateIcon.Classes.Add("spinner");
            UpdateItem.IsEnabled = false;
            try {
                await VhApp.Api.App.VersionCheck(CancellationToken.None);
            }
            catch (Exception ex) {
                await _host.ProcessError(ex);
            }
            finally {
                _host.CloseDrawer();
            }
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnWhatsNewClick(object? sender, RoutedEventArgs e)
    {
        try {
            await Open(ChangelogUrl, Strings.Current.WhatsNew);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnFeedbackClick(object? sender, RoutedEventArgs e)
    {
        try {
            await Open(Strings.Current.SendFeedbackUrl, Strings.Current.SendFeedback);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnPersonalServerClick(object? sender, RoutedEventArgs e)
    {
        try {
            await Open(PersonalServerUrl, Strings.Current.CreatePersonalServer);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnWebsiteClick(object? sender, RoutedEventArgs e)
    {
        try {
            await Open(WebsiteUrl, "vpnhood.com");
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnPrivacyClick(object? sender, RoutedEventArgs e)
    {
        try {
            if (VhApp.Features.PrivacyPolicyUrl is { } url)
                await Open(url.AbsoluteUri, Strings.Current.PrivacyPolicy);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnLinkedInClick(object? sender, RoutedEventArgs e)
    {
        try {
            await Open(LinkedInUrl, "LinkedIn");
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnInstagramClick(object? sender, RoutedEventArgs e)
    {
        try {
            await Open(InstagramUrl, "Instagram");
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnXClick(object? sender, RoutedEventArgs e)
    {
        try {
            await Open(XUrl, "X");
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnPoweredByClick(object? sender, RoutedEventArgs e)
    {
        try {
            await Open(EngineUrl, Strings.Current.VpnhoodEngine);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async Task Open(string url, string title)
    {
        _host.CloseDrawer();
        await _host.OpenLink(new Uri(url), title);
    }
}

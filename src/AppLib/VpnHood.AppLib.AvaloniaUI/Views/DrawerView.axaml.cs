using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using VpnHood.AppLib.AvaloniaUI.Helpers;
using VpnHood.AppLib.AvaloniaUI.Resources;
using VpnHood.Core.Toolkit.ApiClients;

namespace VpnHood.AppLib.AvaloniaUI.Views;

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
    private readonly VpnHoodApp _app = VpnHoodApp.Instance;

    public DrawerView(MainView host)
    {
        _host = host;
        InitializeComponent();

        var features = _app.Features;
        var state = _app.State;
        var logo = AppData.IsConnectApp ? "VpnHoodConnect-logo.png" : "VpnHoodClient-logo.png";
        Logo.Source = AppAssets.Image(logo);
        AppNameText.Text = features.AppName;
        // app.major.minor.build; the web UI adds its own bundle's build as a fourth segment, which
        // this UI has none of
        VersionText.Text = features.Version.ToString(3);

        PremiumItem.IsVisible = !AppData.IsPremiumUser && AppData.IsPremiumSupported && AppData.CanGoPremium;
        AccountItem.IsVisible = features.IsAccountSupported;
        var account = AppData.Account;
        AccountTitle.Text = account != null ? Strings.Current.Account : SignInLabel();
        AccountEmail.Text = account?.Email;
        AccountEmail.IsVisible = account?.Email != null;
        DiagnoseItem.IsEnabled = state.CanDiagnose;
        // the updater is the capability signal: null exactly when no updater was configured
        UpdateItem.IsVisible = state.UpdaterStatus != null;
        PersonalServerItem.IsVisible = !AppData.IsConnectApp;
        PrivacyItem.IsVisible = features.PrivacyPolicyUrl != null;
    }

    // "Sign in with Google" for the one store method, plain "Sign in" with a chooser or none
    private static string SignInLabel()
    {
        var providerId = AppData.PrimaryProviderId;
        if (providerId == null || AppData.HasSignInChoice)
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
        _host.CloseDrawer();
        if (AppData.Account != null) {
            _host.Replace(new AccountView(_host));
            return;
        }
        await _host.ViewModel.SignIn();
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        _host.CloseDrawer();
        _host.Replace(new SettingsView(_host));
    }

    private async void OnDiagnoseClick(object? sender, RoutedEventArgs e)
    {
        _host.CloseDrawer();
        await _host.ViewModel.Diagnose();
    }

    private async void OnUpdateClick(object? sender, RoutedEventArgs e)
    {
        UpdateIcon.Text = Mdi.Loading;
        UpdateIcon.Classes.Add("spinner");
        UpdateItem.IsEnabled = false;
        try {
            var updater = _app.Services.UpdaterService ?? throw new NotSupportedException("App Updater is not supported.");
            await updater.CheckForUpdate(true, CancellationToken.None);
        }
        catch (Exception ex) {
            await _host.ProcessError(ex);
        }
        finally {
            _host.CloseDrawer();
        }
    }

    private async void OnWhatsNewClick(object? sender, RoutedEventArgs e)
    {
        await Open(ChangelogUrl, Strings.Current.WhatsNew);
    }

    private async void OnFeedbackClick(object? sender, RoutedEventArgs e)
    {
        await Open(Strings.Current.SendFeedbackUrl, Strings.Current.SendFeedback);
    }

    private async void OnPersonalServerClick(object? sender, RoutedEventArgs e)
    {
        await Open(PersonalServerUrl, Strings.Current.CreatePersonalServer);
    }

    private async void OnWebsiteClick(object? sender, RoutedEventArgs e)
    {
        await Open(WebsiteUrl, "vpnhood.com");
    }

    private async void OnPrivacyClick(object? sender, RoutedEventArgs e)
    {
        if (_app.Features.PrivacyPolicyUrl is { } url)
            await Open(url.AbsoluteUri, Strings.Current.PrivacyPolicy);
    }

    private async void OnLinkedInClick(object? sender, RoutedEventArgs e)
    {
        await Open(LinkedInUrl, "LinkedIn");
    }

    private async void OnInstagramClick(object? sender, RoutedEventArgs e)
    {
        await Open(InstagramUrl, "Instagram");
    }

    private async void OnXClick(object? sender, RoutedEventArgs e)
    {
        await Open(XUrl, "X");
    }

    private async void OnPoweredByClick(object? sender, RoutedEventArgs e)
    {
        await Open(EngineUrl, Strings.Current.VpnhoodEngine);
    }

    private async Task Open(string url, string title)
    {
        _host.CloseDrawer();
        await _host.OpenLink(new Uri(url), title);
    }
}

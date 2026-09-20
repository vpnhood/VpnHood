using Avalonia.Controls;
using Avalonia.Interactivity;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Resources;
using VpnHood.AppUi.Services;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Helpers;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Views;

// Which text is shown is the head's word (PrivacyConsentAssetName): CONNECT's summary describes
// our servers and their logging, CLIENT's the bring-your-own-key reality, and a fork names its own.
// Whether it is shown is the head's word too (IsLicenseAgreementRequired) and the person's
// (IsLicenseAccepted); until they accept, Back goes nowhere - the page holds the app, as the web
// UI's overlay does.
public partial class PrivacyPolicyView : UserControl, IPage, ILeaveGuard
{
    private readonly MainView _host;

    public PrivacyPolicyView(MainView host)
    {
        _host = host;
        InitializeComponent();
        _ = LoadDocument(AppModel.Features.PrivacyConsentAssetName);
        TermsButton.IsVisible = AppModel.Features.TermsOfUseUrl != null;
        PrivacyButton.IsVisible = AppModel.Features.PrivacyPolicyUrl != null;
    }

    // The document in the app's language, else in English: a language whose translation failed
    // verification ships no file, and the English text beats none - on a consent screen above all.
    // Read through the store's provider, which may be a web server, so it lands after the page shows.
    private async Task LoadDocument(string name)
    {
        try {
            var culture = AppModel.State.CurrentUiCultureInfo.Code;
            foreach (var language in new[] { culture, culture.Split('-')[0], "en" }) {
                if (await AppAssets.ReadTextAsync($"content/{language}/{name}.md", CancellationToken.None) is not { } markdown)
                    continue;

                var (title, markup) = Markdown.Render(markdown);
                TitleText.Text = title;
                RichText.Apply(DocumentText, markup);
                return;
            }

            throw new InvalidOperationException($"The asset store has no content document '{name}' for 'en'.");
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    public void FocusDefault()
    {
        AcceptButton.LandFocus();
    }

    public Task<bool> CanLeave()
    {
        return Task.FromResult(false);
    }

    private async void OnAcceptClick(object? sender, RoutedEventArgs e)
    {
        try {
            var settings = AppModel.UserSettings;
            settings.IsLicenseAccepted = true;
            await AppModel.SaveUserSettings(settings, CancellationToken.None);
            _host.GoHome();
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnTermsClick(object? sender, RoutedEventArgs e)
    {
        try {
            if (AppModel.Features.TermsOfUseUrl is { } url)
                await _host.OpenLink(url, Strings.Current.TermsOfUse);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnPrivacyClick(object? sender, RoutedEventArgs e)
    {
        try {
            if (AppModel.Features.PrivacyPolicyUrl is { } url)
                await _host.OpenLink(url, Strings.Current.PrivacyPolicy);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }
}

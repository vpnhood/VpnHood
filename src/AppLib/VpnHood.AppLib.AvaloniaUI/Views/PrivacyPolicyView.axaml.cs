using Avalonia.Controls;
using Avalonia.Interactivity;
using VpnHood.AppLib.Assets;
using VpnHood.AppLib.AvaloniaUI.Helpers;

namespace VpnHood.AppLib.AvaloniaUI.Views;

// Which text is shown depends on the product: CONNECT's summary describes our servers and their
// logging, CLIENT's the bring-your-own-key reality. Whether it is shown is the head's word
// (IsLicenseAgreementRequired) and the person's (IsLicenseAccepted); until they accept, Back goes
// nowhere - the page holds the app, as the web UI's overlay does.
public partial class PrivacyPolicyView : UserControl, IPage, ILeaveGuard
{
    private readonly MainView _host;

    public PrivacyPolicyView(MainView host)
    {
        _host = host;
        InitializeComponent();
        var (title, markup) = LoadDocument(AppData.IsConnectApp ? "privacy-consent" : "privacy-consent-client");
        TitleText.Text = title;
        RichText.Apply(DocumentText, markup);
        TermsButton.IsVisible = AppData.Features.TermsOfUseUrl != null;
        PrivacyButton.IsVisible = AppData.Features.PrivacyPolicyUrl != null;
    }

    // The document in the app's language, else in English: a language whose translation failed
    // verification ships no file, and the English text beats none - on a consent screen above all.
    private static (string Title, string Markup) LoadDocument(string name)
    {
        var culture = AppData.State.CurrentUiCultureInfo.Code;
        foreach (var language in new[] { culture, culture.Split('-')[0], "en" }) {
            if (AppContent.ReadText($"content/{language}/{name}.md") is { } markdown)
                return Markdown.Render(markdown);
        }
        throw new InvalidOperationException($"The assets folder has no content document '{name}' for 'en'.");
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
            var settings = AppData.UserSettings;
            settings.IsLicenseAccepted = true;
            await AppData.SaveUserSettings(settings, CancellationToken.None);
            _host.GoHome();
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
}

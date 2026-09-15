using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using VpnHood.AppLib.AvaloniaUI.Helpers;
using VpnHood.AppLib.AvaloniaUI.Resources;

namespace VpnHood.AppLib.AvaloniaUI.Views;

// Which text is shown depends on the product: CONNECT's summary describes our servers and their
// logging, CLIENT's the bring-your-own-key reality. Whether it is shown is the head's word
// (IsLicenseAgreementRequired) and the person's (IsLicenseAccepted); until they accept, Back goes
// nowhere - the page holds the app, as the web UI's overlay does.
public partial class PrivacyPolicyView : UserControl, IPage, ILeaveGuard
{
    private readonly MainView _host;
    private readonly VpnHoodApp _app = VpnHoodApp.Instance;

    public PrivacyPolicyView(MainView host)
    {
        _host = host;
        InitializeComponent();
        var (title, markup) = LoadDocument(AppData.IsConnectApp ? "privacy-consent" : "privacy-consent-client");
        TitleText.Text = title;
        RichText.Apply(DocumentText, markup);
        TermsButton.IsVisible = _app.Features.TermsOfUseUrl != null;
        PrivacyButton.IsVisible = _app.Features.PrivacyPolicyUrl != null;
    }

    // The document in the app's language, else in English: a language whose translation failed
    // verification ships no file, and the English text beats none - on a consent screen above all.
    private (string Title, string Markup) LoadDocument(string name)
    {
        var culture = _app.State.CurrentUiCultureInfo.Code;
        foreach (var language in new[] { culture, culture.Split('-')[0], "en" }) {
            if (AppAssets.ReadText($"content/{language}/{name}.md") is { } markdown)
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

    private void OnAcceptClick(object? sender, RoutedEventArgs e)
    {
        _app.UserSettings.IsLicenseAccepted = true;
        _app.SettingsService.Save();
        _host.GoHome();
    }

    private async void OnTermsClick(object? sender, RoutedEventArgs e)
    {
        if (_app.Features.TermsOfUseUrl is { } url)
            await _host.OpenLink(url, Strings.Current.TermsOfUse);
    }

    private async void OnPrivacyClick(object? sender, RoutedEventArgs e)
    {
        if (_app.Features.PrivacyPolicyUrl is { } url)
            await _host.OpenLink(url, Strings.Current.PrivacyPolicy);
    }
}

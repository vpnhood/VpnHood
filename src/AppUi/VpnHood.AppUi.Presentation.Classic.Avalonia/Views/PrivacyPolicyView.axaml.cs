using Avalonia.Controls;
using Avalonia.Interactivity;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Resources;
using VpnHood.AppUi.Common;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Helpers;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Views;

// Which text is shown is the head's word (PrivacyConsentAssetName): CONNECT's summary describes
// our servers and their logging, CLIENT's the bring-your-own-key reality, and a fork names its own.
// Whether it is shown is the head's word too (IsLicenseAgreementRequired) and the person's
// (IsLicenseAccepted); until they accept, the page holds the app: Back leaves the app (IRootPage),
// and the page is back at the next start. Where the law wants a yes before usage data, the app's
// word as well (IsAnonymousTrackerPromptRequired): the page then asks it too, with the box
// unticked, since only the person's own tick counts there.
public partial class PrivacyPolicyView : UserControl, IPage, IRootPage
{
    private readonly MainView _host;

    public PrivacyPolicyView(MainView host)
    {
        _host = host;
        InitializeComponent();
        _ = LoadDocument(VhApp.Features.PrivacyConsentAssetName);
        TermsButton.IsVisible = VhApp.Features.Links.TermsOfUse != null;
        PrivacyButton.IsVisible = VhApp.Features.Links.PrivacyPolicy != null;
        TrackerItem.Title = Strings.Current.AllowAnonymousTracker;
        TrackerItem.Description = Strings.Current.AllowAnonymousTrackerDesc;
        TrackerItem.IsOn = false;
        TrackerItem.IsVisible = VhApp.State.IsAnonymousTrackerPromptRequired;
    }

    // The document as the store hands it over - in the app's language, with the product's own
    // names already in it - rendered to the markup RichText draws. Read through the store's
    // provider, which may be a web server, so it lands after the page shows.
    private async Task LoadDocument(string name)
    {
        try {
            var markdown = await AppAssets.LoadDocumentAsync(name, CancellationToken.None);
            var (title, markup) = Markdown.Render(markdown);
            TitleText.Text = title;
            RichText.Apply(DocumentText, markup);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    public void FocusDefault()
    {
        AcceptButton.LandFocus();
    }

    private async void OnAcceptClick(object? sender, RoutedEventArgs e)
    {
        try {
            var settings = VhApp.UserSettings;
            settings.IsLicenseAccepted = true;
            if (TrackerItem.IsVisible) {
                settings.AllowAnonymousTracker = TrackerItem.IsOn;
                settings.IsAnonymousTrackerPrompted = true;
            }

            await VhApp.SaveUserSettings(settings, CancellationToken.None);
            _host.GoHome();
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnTermsClick(object? sender, RoutedEventArgs e)
    {
        try {
            if (VhApp.Features.Links.TermsOfUse is { } url)
                await _host.OpenLink(url, Strings.Current.TermsOfUse);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnPrivacyClick(object? sender, RoutedEventArgs e)
    {
        try {
            if (VhApp.Features.Links.PrivacyPolicy is { } url)
                await _host.OpenLink(url, Strings.Current.PrivacyPolicy);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }
}

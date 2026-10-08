using System.Text.Json;

namespace VpnHood.AppLib.App.Utils;

// What a product's private appsettings can say; a product derives it for keys of its own. AppOptions
// documents what each key means.
public class AppConfigs
{
    public Uri? PrivacyPolicyUrl { get; set; }
    public Uri? TermsOfUseUrl { get; set; }
    public Uri? WebsiteUrl { get; set; }
    public Uri? WhatsNewUrl { get; set; }
    public Uri? FeedbackUrl { get; set; }
    public Uri? PersonalServerUrl { get; set; }
    public Uri? LinkedInUrl { get; set; }
    public Uri? InstagramUrl { get; set; }
    public Uri? XUrl { get; set; }
    public Uri? RemoteSettingsUrl { get; set; }
    public string? Ga4MeasurementId { get; set; }
    public bool AllowEndPointTracker { get; set; }
    public bool AllowEndPointStrategy { get; set; } = true;
    public string? DefaultAccessKey { get; set; }
    public JsonElement? CustomData { get; set; }
    public Uri? UpdateInfoBaseUrl { get; set; }

    // A head's update feed, named as the publisher names it ("VpnHoodClient-win-x64.json"), or none
    // when UpdateInfoBaseUrl is unsaid.
    public Uri? GetUpdateInfoUrl(string packageTitle, string distribution) => UpdateInfoBaseUrl is null
        ? null
        : new Uri($"{UpdateInfoBaseUrl.AbsoluteUri.TrimEnd('/')}/{packageTitle}-{distribution}.json");
}

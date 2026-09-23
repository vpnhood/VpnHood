using System.Text.Json;

namespace VpnHood.AppLib.App.Utils;

// The settings a product's head must state, one implementation per head beside AppConfigsBase, so a new
// head - ours or a fork's - cannot forget one. What each means, and what null means, AppOptions documents.
public interface IRequiredAppConfigs
{
    public string AppId { get; set; }
    public int? WebUiPort { get; set; }
    public Uri? UpdateInfoUrl { get; set; }
    public string? DefaultAccessKey { get; set; }
    public string? Ga4MeasurementId { get; set; }
    public Uri? RemoteSettingsUrl { get; set; }
    public bool AllowEndPointTracker { get; set; }
    public JsonElement? CustomData { get; set; }

    // Copied onto AppOptions, which documents what these are and what null means.
    public Uri? PrivacyPolicyUrl { get; set; }
    public Uri? TermsOfUseUrl { get; set; }

    // Copied onto AppOptions too: the product's own word in the UI, as paths into its store.
    public string LogoAssetPath { get; set; }
    public string PrivacyConsentAssetName { get; set; }
    public string CompanyName { get; set; }
}
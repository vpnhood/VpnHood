using System.Text.Json;
using VpnHood.App.Client;
using VpnHood.AppLib.Utils;
using VpnHood.Core.Client.Abstractions;

// ReSharper disable StringLiteralTypo
// ReSharper disable CommentTypo
// ReSharper disable HeuristicUnreachableCode
namespace VpnHood.App.Connect.Droid.Web;

internal class AppConfigs : AppConfigsBase<AppConfigs>, IRequiredAppConfigs
{
    public const string AppName = IsDebugMode ? "VpnHOOD! CONNECT (DEBUG)" : "VpnHood! CONNECT";
    public string AppId { get; set; } = Application.Context.PackageName!;

    public Uri? UpdateInfoUrl { get; set; } =
        new("https://github.com/vpnhood/VpnHood.App.Connect/releases/latest/download/VpnHoodConnect-Android-web.json");

    public int? WebUiPort { get; set; } = IsDebugMode ? 7701 : 7770;
    public string? DefaultAccessKey { get; set; } = IsDebugMode ? ClientOptions.SampleAccessKey : null;
    public string? Ga4MeasurementId { get; set; }
    public Uri? RemoteSettingsUrl { get; set; }
    public bool AllowEndPointTracker { get; set; }
    public JsonElement? CustomData { get; set; }

    public string? AppsFlyerDevKey { get; set; }

    public static AppConfigs Load()
    {
        var appConfigs = new AppConfigs();
        appConfigs.LoadConfig();

        // The default access key is embedded as its own resource (per configuration) so it can be
        // sourced from a GitHub secret. When present it overrides the in-code/json default.
        var accessKey = appConfigs.ReadResourceText("access_key_default.txt");
        if (!string.IsNullOrWhiteSpace(accessKey))
            appConfigs.DefaultAccessKey = accessKey.Trim();

        return appConfigs;
    }

#if DEBUG
    public const bool IsDebugMode = true;
#else
    public const bool IsDebugMode = false;
#endif
}
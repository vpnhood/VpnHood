using System.Text.Json;
using VpnHood.AppLib.Utils;
using VpnHood.Core.Client.Abstractions;

// ReSharper disable StringLiteralTypo
// ReSharper disable CommentTypo
// ReSharper disable HeuristicUnreachableCode
namespace VpnHood.App.Client.Droid.Google;

internal class AppConfigs : AppConfigsBase<AppConfigs>, IRequiredAppConfigs
{
    public const string AppName = IsDebugMode ? "VpnHOOD! CLIENT (DEBUG)" : "VpnHood! CLIENT";
    public string AppId { get; set; } = Application.Context.PackageName!;
    public Uri? UpdateInfoUrl { get; set; } = new("https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodClient-android.json");
    public int? WebUiPort { get; set; } = IsDebugMode ? 9581 : 9580;
    public string? DefaultAccessKey { get; set; } = IsDebugMode ? ClientOptions.SampleAccessKey : null;
    public string? Ga4MeasurementId { get; set; }
    public Uri? RemoteSettingsUrl { get; set; }
    public bool AllowEndPointTracker { get; set; }
    public JsonElement? CustomData { get; set; }

    public static AppConfigs Load()
    {
        var appConfigs = new AppConfigs();
        appConfigs.Merge("AppSettings");
        appConfigs.Merge("AppSettings_Environment");
        return appConfigs;
    }

#if DEBUG
    public const bool IsDebugMode = true;
#else
    public const bool IsDebugMode = false;
#endif
}
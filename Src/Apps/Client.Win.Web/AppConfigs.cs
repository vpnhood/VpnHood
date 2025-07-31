using System.Text.Json;
using VpnHood.AppLib.Utils;
using VpnHood.Core.Client.Abstractions;

// ReSharper disable HeuristicUnreachableCode
// ReSharper disable StringLiteralTypo
// ReSharper disable CommentTypo
namespace VpnHood.App.Client.Win.Web;

internal class AppConfigs : AppConfigsBase<AppConfigs>
{
    public const string AppName = IsDebugMode ? "VpnHOOD! CLIENT (DEBUG)" : "VpnHood! CLIENT";

    public string AppId { get; set; } =
        IsDebugMode ? "com.vpnhood.client.windows.debug" : "com.vpnhood.client.windows";

    public string StorageFolderName { get; set; } = IsDebugMode ? "VpnHoodClient.debug" : "VpnHood";

    public string? UpdateInfoUrl { get; set; } =
        "https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodClient-win-x64.json";

    public int? SpaDefaultPort { get; set; } = IsDebugMode ? 9571 : 80;
    public bool SpaListenToAllIps { get; set; } = IsDebugMode;
    public string? Ga4MeasurementId { get; set; }

    // SampleAccessKey is a test access key, you should replace it with your own access key.
    // It is limited and can not be used in production.
    public string DefaultAccessKey { get; set; } = ClientOptions.SampleAccessKey;
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
    public static bool IsDebug => IsDebugMode;

}
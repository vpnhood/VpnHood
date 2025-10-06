using System.Text.Json;
using VpnHood.App.Client;
using VpnHood.AppLib.Utils;
using VpnHood.Core.Client.Abstractions;

// ReSharper disable HeuristicUnreachableCode
// ReSharper disable StringLiteralTypo
// ReSharper disable CommentTypo
namespace VpnHood.App.Connect.Linux.Web;

internal class AppConfigs : AppConfigsBase<AppConfigs>, IRequiredAppConfigs
{
    public const string AppTitle = IsDebugMode ? "VpnHOOD! CONNECT (DEBUG)" : "VpnHood! CONNECT";
    // currently can not have space or more than 20 characters in linux app name as it used for adapter name
    public const string AppName = IsDebugMode ? "VpnHOODConnect_dbg" : "VpnHoodConnect";
    public string AppId { get; set; } = IsDebugMode ? "com.vpnhood.connect.linux.debug" : "com.vpnhood.connect.linux";
    public Uri? UpdateInfoUrl { get; set; } = null;
    public int? WebUiPort { get; set; } = IsDebugMode ? 9571 : 7070;
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
    public static bool IsDebug => IsDebugMode;
}
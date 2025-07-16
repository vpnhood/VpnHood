using VpnHood.AppLib.Utils;
using VpnHood.Core.Client.Abstractions;

// ReSharper disable HeuristicUnreachableCode

namespace VpnHood.App.Client.Linux.Web;

internal class AppConfigs : AppConfigsBase<AppConfigs>
{
    //public const string AppName = IsDebugMode ? "VpnHOOD! CLIENT (DEBUG)" : "VpnHood! CLIENT";
    // currently can not have space or more than 20 characters in linux app name as it used for adapter name
    public const string AppName = "VpnHoodClient"; 

    public string AppId { get; set; } =
        IsDebugMode ? "com.vpnhood.client.linux.debug" : "com.vpnhood.client.linux";

    public string StorageFolderName { get; set; } = IsDebugMode ? "VpnHoodClient.debug" : "VpnHoodClient";

    public string? UpdateInfoUrl { get; set; } = null;
        //"https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodClient-win-x64.json";

    public int? SpaDefaultPort { get; set; } = IsDebugMode ? 9571 : 80;
    public bool SpaListenToAllIps { get; set; } = IsDebugMode;
    public string? Ga4MeasurementId { get; set; }

    // SampleAccessKey is a test access key, you should replace it with your own access key.
    // It is limited and can not be used in production.
    public string DefaultAccessKey { get; set; } = ClientOptions.SampleAccessKey;

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
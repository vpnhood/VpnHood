using VpnHood.AppLib.Utils;
using VpnHood.Core.Client;

// ReSharper disable StringLiteralTypo
// ReSharper disable CommentTypo
namespace VpnHood.App.Client.Droid.Web;

internal class AppConfigs : AppConfigsBase<AppConfigs>
{
    public string AppName { get; init; } = IsDebugMode ? "VpnHOOD! CLIENT (DEBUG)" : "VpnHood! CLIENT";
    public Uri? UpdateInfoUrl { get; init; } = new ("https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodClient-android-web.json");
    public int? SpaDefaultPort { get; init; } = IsDebugMode ? 9581 : 9580;
    public bool SpaListenToAllIps { get; init; } = IsDebugMode;
    
    // SampleAccessKey is a test access key, you should replace it with your own access key.
    // It is limited and can not be used in production.
    public string? DefaultAccessKey { get; init; } = IsDebugMode ? ClientOptions.SampleAccessKey : null;

    public static AppConfigs Load()
    {
        var appConfigs = new AppConfigs();
        appConfigs.Merge("AppSettings");
        appConfigs.Merge("AppSettings_Environment");
        return appConfigs;
    }

#if DEBUG
    public static bool IsDebugMode => true;
#else
    public static bool IsDebugMode => false;
#endif
}
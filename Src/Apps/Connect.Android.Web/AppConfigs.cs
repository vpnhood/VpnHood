using VpnHood.AppFramework.Utils;
using VpnHood.Client;

// ReSharper disable StringLiteralTypo
// ReSharper disable CommentTypo
namespace VpnHood.Apps.Connect.Droid.Web;

internal class AppConfigs : AppConfigsBase<AppConfigs>
{
    public string AppName { get; init; } = IsDebugMode ? "VpnHOOD! CONNECT (DEBUG)" : "VpnHood! CONNECT";
    public Uri? UpdateInfoUrl { get; init; } = new("https://github.com/vpnhood/VpnHood.AppFramework.Connect/releases/latest/download/VpnHoodConnect-Android-web.json");
    public int? SpaDefaultPort { get; init; } = IsDebugMode ? 9571 : 9570;
    public bool SpaListenToAllIps { get; init; } = IsDebugMode;
    public bool AllowEndPointTracker { get; init; } = true;
    public string? Ga4MeasurementId { get; init; }
    // SampleAccessKey is a test access key, you should replace it with your own access key.
    // It is limited and can not be used in production.
    public string DefaultAccessKey { get; init; } = ClientOptions.SampleAccessKey;

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
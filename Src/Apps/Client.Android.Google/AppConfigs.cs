using VpnHood.AppLib.Utils;
using VpnHood.Core.Client.Abstractions;

// ReSharper disable StringLiteralTypo
// ReSharper disable CommentTypo
// ReSharper disable HeuristicUnreachableCode
namespace VpnHood.App.Client.Droid.Google;

internal class AppConfigs : AppConfigsBase<AppConfigs>
{
    public const string AppName = IsDebugMode ? "VpnHOOD! CLIENT (DEBUG)" : "VpnHood! CLIENT";

    public string? UpdateInfoUrl { get; init; } =
        "https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodClient-android.json";

    public int? SpaDefaultPort { get; init; } = IsDebugMode ? 9581 : 9580;
    public bool SpaListenToAllIps { get; init; } = false;

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
    public const bool IsDebugMode = true;
#else
    public const bool IsDebugMode = false;
#endif
}
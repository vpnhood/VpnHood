using VpnHood.AppLib.Utils;
using VpnHood.Core.Client.Abstractions;

// ReSharper disable StringLiteralTypo
// ReSharper disable CommentTypo
// ReSharper disable HeuristicUnreachableCode
namespace VpnHood.App.Connect.Droid.Web;

internal class AppConfigs : AppConfigsBase<AppConfigs>
{
    public const string AppName = IsDebugMode ? "VpnHOOD! CONNECT (DEBUG)" : "VpnHood! CONNECT";

    public string? UpdateInfoUrl { get; init; } =
        "https://github.com/vpnhood/VpnHood.App.Connect/releases/latest/download/VpnHoodConnect-Android-web.json";

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
    public const bool IsDebugMode = true;
#else
    public const bool IsDebugMode = false;
#endif
}
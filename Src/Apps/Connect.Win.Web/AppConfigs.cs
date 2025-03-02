using VpnHood.AppLib.Utils;
using VpnHood.Core.Client.Abstractions;

// ReSharper disable HeuristicUnreachableCode
// ReSharper disable StringLiteralTypo
// ReSharper disable CommentTypo
namespace VpnHood.App.Connect.Win.Web;

internal class AppConfigs : AppConfigsBase<AppConfigs>
{
    public const string AppName = IsDebugMode ? "VpnHOOD! CONNECT (DEBUG)" : "VpnHood! CONNECT";

    public string? UpdateInfoUrl { get; set; } =
        "https://github.com/vpnhood/VpnHood.App.Connect/releases/latest/download/VpnHoodConnect-win-x64.json";

    public int? SpaDefaultPort { get; set; } = IsDebugMode ? 9571 : 80;
    public bool SpaListenToAllIps { get; set; } = IsDebugMode;

    // SampleAccessKey is a test access key, you should replace it with your own access key.
    // It is limited and can not be used in production.
    public string DefaultAccessKey { get; set; } = ClientOptions.SampleAccessKey;
    public bool AllowEndPointTracker { get; set; } = true;
    public string? Ga4MeasurementId { get; set; }

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
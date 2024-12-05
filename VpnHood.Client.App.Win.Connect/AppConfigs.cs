using VpnHood.Common.Utils;

// ReSharper disable StringLiteralTypo
// ReSharper disable CommentTypo

namespace VpnHood.Client.App.Win.Connect;

internal class AppConfigs : Singleton<AppConfigs>
{
    public string? UpdateInfoUrl { get; init; }
    public bool ListenToAllIps { get; init; } = IsDebugMode;
    public bool AllowEndPointTracker { get; init; }
    public int DefaultSpaPort { get; init; } = IsDebugMode ? 9571 : 80;
    public string? Ga4MeasurementId { get; init; }

    // This is a test access key, you should replace it with your own access key.
    // It is limited and can not be used in production.
    public string DefaultAccessKey { get; init; } = ClientOptions.SampleAccessKey;

    public static AppConfigs Load()
    {
        var appConfigs = new AppConfigs();
        appConfigs.Merge("AppSettings");
        appConfigs.Merge("AppSettings_Environment");
        return appConfigs;
    }

    private void Merge(string configName)
    {
        var json = VhUtil.GetAssemblyMetadata(typeof(AppConfigs).Assembly, configName, "");
        if (!string.IsNullOrEmpty(json)) 
            JsonSerializerExt.PopulateObject(this, json);
    }

    public static bool IsDebugMode {
        get {
#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }
}
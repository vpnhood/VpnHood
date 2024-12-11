using VpnHood.Common.Utils;

// ReSharper disable StringLiteralTypo
// ReSharper disable CommentTypo

namespace VpnHood.Client.App.Win.Connect;

internal class AppConfigs : Singleton<AppConfigs>
{
    // SampleAccessKey is a test access key, you should replace it with your own access key.
    // It is limited and can not be used in production.
    public string DefaultAccessKey { get; init; } = ClientOptions.SampleAccessKey;

    public string? Ga4MeasurementId { get; init; }

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
}
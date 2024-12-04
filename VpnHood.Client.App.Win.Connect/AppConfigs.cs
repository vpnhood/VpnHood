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
    public string DefaultAccessKey { get; init; } =
        "vh://eyJ2Ijo0LCJuYW1lIjoiVnBuSG9vZCBHbG9iYWwgLSBXaW4iLCJzaWQiOiIxNTE2IiwidGlkIjoiMDE5MzkxMTYtZmNjZC03OTJjLWIwZDgtOWFlZDM1NjljY2ZjIiwiaWF0IjoiMjAyNC0xMi0wNFQyMzowNTo0OC4xNTI3NjUxWiIsInNlYyI6InBHNU5FSjBZcnl0cXJPQlhYZEo3cGc9PSIsInNlciI6eyJjdCI6IjIwMjQtMTItMDRUMjA6NTk6MDVaIiwiaG5hbWUiOiJkb3dubG9hZC5taWNyb3NvZnQuY29tIiwiaHBvcnQiOjAsImlzdiI6ZmFsc2UsInNlYyI6InZhQnFVOVJDM1FIYVc0eEY1aWJZRnc9PSIsImNoIjoiOURQUTYvcmRySmFybU90NmQyVHRRMmE2cjlzPSIsInVybCI6Imh0dHBzOi8vZ2l0bGFiLmNvbS92cG5ob29kL1Zwbkhvb2QuRmFybUtleXMvLS9yYXcvbWFpbi9HbG9iYWxfRmFybV9lbmNyeXB0ZWRfdG9rZW4udHh0IiwidXJscyI6WyJodHRwczovL2dpdGxhYi5jb20vdnBuaG9vZC9WcG5Ib29kLkZhcm1LZXlzLy0vcmF3L21haW4vR2xvYmFsX0Zhcm1fZW5jcnlwdGVkX3Rva2VuLnR4dCIsImh0dHBzOi8vYml0YnVja2V0Lm9yZy92cG5ob29kL3Zwbmhvb2QuZmFybWtleXMvcmF3L21haW4vR2xvYmFsX0Zhcm1fZW5jcnlwdGVkX3Rva2VuLnR4dCIsImh0dHBzOi8vcmF3LmdpdGh1YnVzZXJjb250ZW50LmNvbS92cG5ob29kL1Zwbkhvb2QuRmFybUtleXMvbWFpbi9HbG9iYWxfRmFybV9lbmNyeXB0ZWRfdG9rZW4udHh0Il0sImVwIjpbIjUxLjgxLjgxLjI1MDo0NDMiLCJbMjYwNDoyZGMwOjEwMToyMDA6OjkzZV06NDQzIiwiMTUuMjA0Ljg3LjkwOjQ0MyIsIjgyLjE4MC4xNDcuMTk0OjQ0MyIsIlsyNDAwOmQzMjE6MjIxOTo4NTQ5OjoxXTo0NDMiLCIxOTQuMTY0LjEyNi43MDo0NDMiLCJbMmEwMDpkYTAwOmY0MGQ6MzMwMDo6MV06NDQzIiwiNTEuODEuMjEwLjE2NDo0NDMiLCJbMjYwNDoyZGMwOjIwMjozMDA6OjVjZV06NDQzIiwiNTEuODEuMjI0LjIxNjo0NDMiLCI1MS43OS43My4yNDA6NDQzIiwiWzI2MDc6NTMwMDoyMDU6MjAwOjo1M2IwXTo0NDMiLCI1MS44MS42OS4xNTQ6NDQzIiwiNTcuMTI4LjIwMC4xMzk6NDQzIiwiWzIwMDE6NDFkMDo2MDE6MTEwMDo6MTNhNF06NDQzIiwiMTk0LjI0Ni4xMTQuMjI6NDQzIiwiNS4yNTAuMTkwLjg6NDQzIiwiWzIwMDE6YmEwOjIyZDplZDAwOjoxXTo0NDMiXSwibG9jIjpbIkFNL1llcmV2YW4iLCJBVS9OZXcgU291dGggV2FsZXMiLCJCUi9TYW8gUGF1bG8iLCJDQS9RdWViZWMiLCJGUi9IYXV0cy1kZS1GcmFuY2UiLCJERS9CZXJsaW4iLCJISy9Ib25nIEtvbmciLCJJTi9NYWhhcmFzaHRyYSIsIkpQL1Rva3lvIiwiS1ovQWxtYXR5IiwiTVgvUXVlcmV0YXJvIiwiUEwvTWF6b3ZpYSIsIlJVL01vc2NvdyIsIlNHL1NpbmdhcG9yZSIsIlpBL0dhdXRlbmciLCJFUy9NYWRyaWQiLCJUUi9CdXJzYSBQcm92aW5jZSIsIkFFL0R1YmFpIiwiR0IvRW5nbGFuZCAiLCJVUy9PcmVnb24gIiwiVVMvVmlyZ2luaWEgIl0sImxvYzIiOlsiQU0vWWVyZXZhbiIsIkFVL05ldyBTb3V0aCBXYWxlcyIsIkJSL1NhbyBQYXVsbyIsIkNBL1F1ZWJlYyIsIkZSL0hhdXRzLWRlLUZyYW5jZSIsIkRFL0JlcmxpbiIsIkhLL0hvbmcgS29uZyIsIklOL01haGFyYXNodHJhIiwiSlAvVG9reW8iLCJLWi9BbG1hdHkiLCJNWC9RdWVyZXRhcm8iLCJQTC9NYXpvdmlhIiwiUlUvTW9zY293IiwiU0cvU2luZ2Fwb3JlIiwiWkEvR2F1dGVuZyIsIkVTL01hZHJpZCIsIlRSL0J1cnNhIFByb3ZpbmNlIiwiQUUvRHViYWkiLCJHQi9FbmdsYW5kIFsjdW5ibG9ja2FibGVdIiwiVVMvT3JlZ29uIFt+I3VuYmxvY2thYmxlXSIsIlVTL1ZpcmdpbmlhIFt+I3VuYmxvY2thYmxlXSJdfSwidGFncyI6W10sImlzcHViIjp0cnVlLCJjcG9scyI6W3siY2NzIjpbIioiXSwiZnJlZSI6W10sInBidCI6MTAsInBiYyI6dHJ1ZX0seyJjY3MiOlsiQ04iLCJJUiJdLCJmcmVlIjpbXSwidW8iOnRydWUsInBidCI6MTAsInBiYyI6dHJ1ZX1dfQ==";
        //ClientOptions.SampleAccessKey;

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
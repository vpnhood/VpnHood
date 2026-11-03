using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppLib.Abstractions;

public static class AppFileStoreExtensions
{
    // A file's object, or default where there is none to take: no file yet - a first run's - goes unlogged,
    // and one that can't be read is logged.
    public static T? TryReadJson<T>(this IAppFileStore fileStore, string filePath, ILogger? logger = null)
    {
        try {
            var json = fileStore.Read(filePath);
            return json is null ? default : JsonUtils.Deserialize<T>(json);
        }
        catch (Exception ex) {
            logger?.LogError(ex, "Could not read json file. FilePath: {FilePath}", filePath);
            return default;
        }
    }
}

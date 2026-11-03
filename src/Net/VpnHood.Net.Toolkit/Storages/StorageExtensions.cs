using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Net.Toolkit.Storages;

public static class StorageExtensions
{
    // A file's object, or default where there is none to take: no file yet - a first run's - goes unlogged,
    // and one that can't be read is logged.
    public static T? TryReadJson<T>(this Storage storage, string name, ILogger? logger = null)
    {
        try {
            var json = storage.Read(name);
            return json is null ? default : JsonUtils.Deserialize<T>(json);
        }
        catch (Exception ex) {
            logger?.LogError(ex, "Could not read json file. FilePath: {FilePath}",
                Path.Combine(storage.FolderPath, name));
            return default;
        }
    }
}

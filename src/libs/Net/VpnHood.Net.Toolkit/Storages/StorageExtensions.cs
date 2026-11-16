using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Net.Toolkit.Storages;

/// <summary>
/// Provides JSON reading for a <see cref="Storage"/>.
/// </summary>
public static class StorageExtensions
{
    /// <summary>
    /// Reads a file's JSON as an object, or the default where there is none to take: a missing file, as on a first run,
    /// goes unlogged, and one that cannot be read or parsed is logged.
    /// </summary>
    /// <typeparam name="T">The object's type.</typeparam>
    /// <param name="storage">The storage.</param>
    /// <param name="name">The file's name.</param>
    /// <param name="logger">The logger a failure goes to, or <see langword="null"/> for none.</param>
    /// <returns>The object, or the default.</returns>
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

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Net.Toolkit.Utils;

/// <summary>
/// Provides JSON helpers: comparison, cloning, reading text and files, and redaction.
/// </summary>
public static class JsonUtils
{
    /// <summary>Determines whether two objects write the same JSON, with the default settings.</summary>
    /// <param name="obj1">The first object, or <see langword="null"/>.</param>
    /// <param name="obj2">The second object, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if both are null or their JSON is the same; otherwise, <see langword="false"/>.</returns>
    public static bool JsonEquals(object? obj1, object? obj2)
    {
        if (obj1 == null && obj2 == null) return true;
        if (obj1 == null || obj2 == null) return false;
        return JsonSerializer.Serialize(obj1) == JsonSerializer.Serialize(obj2);
    }

    /// <summary>Copies an object by writing it as JSON and reading it back.</summary>
    /// <typeparam name="T">The object's type.</typeparam>
    /// <param name="obj">The object.</param>
    /// <param name="options">The JSON settings, or <see langword="null"/> for the defaults.</param>
    /// <returns>The copy.</returns>
    /// <exception cref="InvalidDataException">The result is <see langword="null"/>.</exception>
    /// <exception cref="JsonException">The text is not JSON of the type.</exception>
    public static T JsonClone<T>(T obj, JsonSerializerOptions? options = null)
    {
        var json = JsonSerializer.Serialize(obj, options);
        return Deserialize<T>(json, options);
    }

    /// <summary>Reads an object from JSON; a <see langword="null"/> result is refused.</summary>
    /// <typeparam name="T">The object's type.</typeparam>
    /// <param name="json">The JSON.</param>
    /// <param name="options">The JSON settings, or <see langword="null"/> for the defaults.</param>
    /// <returns>The object.</returns>
    /// <exception cref="InvalidDataException">The result is <see langword="null"/>.</exception>
    /// <exception cref="JsonException">The text is not JSON of the type.</exception>
    public static T Deserialize<T>(string json, JsonSerializerOptions? options = null)
    {
        return JsonSerializer.Deserialize<T>(json, options) ??
               throw new InvalidDataException($"{typeof(T)} could not be deserialized!");
    }

    /// <summary>Reads an object from JSON with the type's metadata; a <see langword="null"/> result is refused.</summary>
    /// <typeparam name="T">The object's type.</typeparam>
    /// <param name="json">The JSON.</param>
    /// <param name="jsonTypeInfo">The type's metadata.</param>
    /// <returns>The object.</returns>
    /// <exception cref="InvalidDataException">The result is <see langword="null"/>.</exception>
    /// <exception cref="JsonException">The text is not JSON of the type.</exception>
    public static T Deserialize<T>(string json, JsonTypeInfo<T> jsonTypeInfo)
    {
        return JsonSerializer.Deserialize(json, jsonTypeInfo) ??
               throw new InvalidDataException($"{typeof(T)} could not be deserialized!");
    }

    /// <summary>Reads an object from a JSON file; a <c>null</c> is refused.</summary>
    /// <typeparam name="T">The object's type.</typeparam>
    /// <param name="filePath">The file's path.</param>
    /// <param name="options">The JSON settings, or <see langword="null"/> for the defaults.</param>
    /// <returns>The object.</returns>
    /// <exception cref="IOException">The file cannot be read.</exception>
    /// <exception cref="UnauthorizedAccessException">The process may not read the file.</exception>
    /// <exception cref="InvalidDataException">The result is <see langword="null"/>.</exception>
    /// <exception cref="JsonException">The text is not JSON of the type.</exception>
    public static T DeserializeFile<T>(string filePath, JsonSerializerOptions? options = null)
    {
        var json = File.ReadAllText(filePath);
        var obj = Deserialize<T>(json, options);
        return obj;
    }

    /// <summary>
    /// Reads an object from a JSON file, or gives the default: a missing file, as on a first run, goes unlogged, and any
    /// other failure, a file gone after the check included, is logged as an error when there is a logger.
    /// </summary>
    /// <typeparam name="T">The object's type.</typeparam>
    /// <param name="filePath">The file's path.</param>
    /// <param name="options">The JSON settings, or <see langword="null"/> for the defaults.</param>
    /// <param name="logger">The logger a failure goes to, or <see langword="null"/> for none.</param>
    /// <returns>The object, or the default.</returns>
    public static T? TryDeserializeFile<T>(string filePath, JsonSerializerOptions? options = null,
        ILogger? logger = null)
    {
        // no file yet is the normal case, a first run's, not an error to log
        if (!File.Exists(filePath))
            return default;

        try {
            return DeserializeFile<T>(filePath, options);
        }
        catch (Exception ex) {
            logger?.LogError(ex, "Could not read json file. FilePath: {FilePath}", filePath);
            return default;
        }
    }

    /// <summary>
    /// Reads an object from a JSON file asynchronously, with the default settings, or gives the default: a missing file
    /// goes unlogged, a read failure is logged as a warning and a parse failure as an error, to <c>VhLogger</c>.
    /// </summary>
    /// <typeparam name="T">The object's type.</typeparam>
    /// <param name="filePath">The file's path.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the object, or the default.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static async Task<T?> TryDeserializeFileAsync<T>(string filePath, CancellationToken cancellationToken)
    {
        var json = await FileUtils.TryReadAllTextAsync(filePath, cancellationToken).Vhc();
        if (json == null)
            return default;

        try {
            return Deserialize<T>(json);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not read json file. FilePath: {FilePath}", filePath);
            return default;
        }
    }

    /// <summary>
    /// Replaces the values of some keys in JSON text with <c>"***"</c>, an array's with <c>["***"]</c>, by pattern
    /// matching, not by parsing.
    /// </summary>
    /// <param name="json">The JSON text.</param>
    /// <param name="keys">The keys whose values are hidden; they are used in the patterns as they are.</param>
    /// <returns>The text with those values hidden.</returns>
    public static string RedactValue(string json, string[] keys)
    {
        foreach (var key in keys) {
            // array
            var jsonLength = json.Length;
            var pattern = @"""key""\s*:\s*\[[^\]]*\]".Replace("key", key);
            json = Regex.Replace(json, pattern, $"\"{key}\": [\"***\"]");
            if (jsonLength != json.Length)
                continue;

            // single
            pattern = "(?<=\"key\":)[^,|}|\r]+(?=,|}|\r)".Replace("key", key);
            json = Regex.Replace(json, pattern, " \"***\"");
        }

        return json;
    }
}
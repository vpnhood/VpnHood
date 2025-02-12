using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace VpnHood.Core.Common.Utils;

public static class JsonUtils
{
    public static bool JsonEquals(object? obj1, object? obj2)
    {
        if (obj1 == null && obj2 == null) return true;
        if (obj1 == null || obj2 == null) return false;
        return JsonSerializer.Serialize(obj1) == JsonSerializer.Serialize(obj2);
    }

    public static T JsonClone<T>(T obj, JsonSerializerOptions? options = null)
    {
        var json = JsonSerializer.Serialize(obj, options);
        return Deserialize<T>(json, options);
    }

    public static T Deserialize<T>(string json, JsonSerializerOptions? options = null)
    {
        return JsonSerializer.Deserialize<T>(json, options) ??
               throw new InvalidDataException($"{typeof(T)} could not be deserialized!");
    }

    public static T DeserializeFile<T>(string filePath, JsonSerializerOptions? options = null)
    {
        var json = File.ReadAllText(filePath);
        var obj = Deserialize<T>(json, options);
        return obj;
    }

    public static T? TryDeserializeFile<T>(string filePath, JsonSerializerOptions? options = null,
        ILogger? logger = null)
    {
        try {
            return DeserializeFile<T>(filePath, options);
        }
        catch (Exception ex) {
            logger?.LogError(ex, "Could not read json file. FilePath: {FilePath}", filePath);
            return default;
        }
    }

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
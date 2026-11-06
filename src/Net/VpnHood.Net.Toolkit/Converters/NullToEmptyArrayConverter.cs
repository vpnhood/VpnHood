using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace VpnHood.Net.Toolkit.Converters;

public class NullToEmptyArrayConverter<T> : JsonConverter<T[]>
{
    public override bool HandleNull => true;

    public override T[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return [];

        return JsonSerializer.Deserialize(ref reader, GetArrayTypeInfo(options)) ??
               throw new JsonException($"Could not parse {typeof(T)}");
    }

    public override void Write(Utf8JsonWriter writer, T[] value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, GetArrayTypeInfo(options));
    }

    // From the settings' own resolver: the overloads that take the settings need reflection.
    private static JsonTypeInfo<T[]> GetArrayTypeInfo(JsonSerializerOptions options)
    {
        return (JsonTypeInfo<T[]>)options.GetTypeInfo(typeof(T[]));
    }
}
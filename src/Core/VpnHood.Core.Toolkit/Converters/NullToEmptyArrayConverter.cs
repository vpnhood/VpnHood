using System.Text.Json;
using System.Text.Json.Serialization;

namespace VpnHood.Core.Toolkit.Converters;

public class NullToEmptyArrayConverter<T> : JsonConverter<T[]>
{
    public override bool HandleNull => true;

    public override T[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return [];

        return JsonSerializer.Deserialize<T[]>(ref reader, options) ??
               throw new JsonException($"Could not parse {typeof(T)}");
    }

    public override void Write(Utf8JsonWriter writer, T[] value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, options);
    }
}
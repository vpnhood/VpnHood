using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace VpnHood.Net.Toolkit.Converters;

/// <summary>
/// Converts an array, reading JSON null as an empty array.
/// </summary>
/// <remarks>It can be set on a property or added to the settings' converters, for every array of the type.</remarks>
/// <typeparam name="T">The type of the items.</typeparam>
public class NullToEmptyArrayConverter<T> : JsonConverter<T[]>
{
    /// <inheritdoc/>
    /// <remarks>It is <see langword="true"/>: the converter reads null itself.</remarks>
    public override bool HandleNull => true;

    /// <inheritdoc/>
    /// <exception cref="JsonException">The value is not an array.</exception>
    public override T[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return [];

        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException($"Could not parse {typeof(T)}[]. TokenType: {reader.TokenType}");

        // Item by item, never the array through the settings: with this converter among the settings' own, the
        // array's serializer is this converter again, without end.
        var itemTypeInfo = GetItemTypeInfo(options);
        var items = new List<T>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            items.Add(JsonSerializer.Deserialize(ref reader, itemTypeInfo)!);

        return items.ToArray();
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, T[]? value, JsonSerializerOptions options)
    {
        if (value == null) {
            writer.WriteNullValue();
            return;
        }

        var itemTypeInfo = GetItemTypeInfo(options);
        writer.WriteStartArray();
        foreach (var item in value)
            JsonSerializer.Serialize(writer, item, itemTypeInfo);

        writer.WriteEndArray();
    }

    // From the settings' own resolver: the overloads that take the settings need reflection.
    private static JsonTypeInfo<T> GetItemTypeInfo(JsonSerializerOptions options)
    {
        return (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
    }
}
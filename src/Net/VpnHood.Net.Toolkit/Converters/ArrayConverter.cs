using System.Text.Json;
using System.Text.Json.Serialization;

namespace VpnHood.Net.Toolkit.Converters;

/// <summary>
/// Converts an array to and from a JSON array, each item through another converter.
/// </summary>
/// <typeparam name="T">The type of the items.</typeparam>
/// <typeparam name="TConverter">The converter of the items.</typeparam>
public class ArrayConverter<T, TConverter> : JsonConverter<T[]> where TConverter : JsonConverter<T>, new()
{
    private readonly TConverter _typeConverter = new();

    /// <inheritdoc/>
    /// <exception cref="JsonException">The value is not an array.</exception>
    public override T[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException();

        reader.Read();

        var elements = new List<T>();
        while (reader.TokenType != JsonTokenType.EndArray) {
            elements.Add(_typeConverter.Read(ref reader, typeof(T), options)!);
            reader.Read();
        }

        return [.. elements];
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, T[] value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();

        foreach (var item in value)
            _typeConverter.Write(writer, item, options);

        writer.WriteEndArray();
    }
}
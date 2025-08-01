﻿using System.Text.Json;
using System.Text.Json.Serialization;

namespace VpnHood.Core.Toolkit.Converters;

public class ArrayConverter<T, TConverter> : JsonConverter<T[]> where TConverter : JsonConverter<T>, new()
{
    private readonly TConverter _typeConverter = new();

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

        return elements.ToArray();
    }

    public override void Write(Utf8JsonWriter writer, T[] value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();

        foreach (var item in value)
            _typeConverter.Write(writer, item, options);

        writer.WriteEndArray();
    }
}